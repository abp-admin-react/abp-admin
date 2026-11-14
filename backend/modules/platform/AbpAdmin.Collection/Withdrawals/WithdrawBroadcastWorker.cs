using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Withdrawals;

/// <summary>
/// 提现后台任务与提交共用提现总开关。执行轮读取最新的广播间隔和批量配置，
/// 再交由 <see cref="WithdrawBroadcastManager"/> 核对回执、审核和广播。
/// </summary>
public class WithdrawBroadcastWorker : CollectionChainWorkerBase
{
    public WithdrawBroadcastWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        CollectionChainWorkerOptions options,
        ILogger<WithdrawBroadcastWorker> logger)
        : base(distributedLock, scopeFactory, configuration,
            TimeSpan.FromSeconds(WithdrawDefaults.BroadcastIntervalSeconds),
            TimeSpan.FromSeconds(options.WithdrawRoundBudgetSeconds),
            logger)
    {
    }

    protected override string LockKey => CollectionConsts.WithdrawBroadcastLockKey;

    protected override int? GetRoundBudgetSeconds(ChainRuntimeSettingsDto? runtime) => runtime?.WithdrawRoundBudgetSeconds;

    /// <summary>测试桥：宿主注册测试断言轮锁键（internal + InternalsVisibleTo）。</summary>
    internal string LockKeyExposeForTest() => LockKey;

    protected override async Task<WorkerRoundPlan> GetRoundPlanAsync(IServiceProvider scopedServices)
    {
        var settings = scopedServices.GetRequiredService<ISettingProvider>();
        return WorkerRoundPlan.Create(
            await settings.IsTrueAsync(CollectionSettings.Withdraw.Enabled),
            TimeSpan.FromSeconds(await settings.GetIntAsync(
                CollectionSettings.Withdraw.BroadcastIntervalSeconds, WithdrawDefaults.BroadcastIntervalSeconds)));
    }

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContextProvider = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var manager = scopedServices.GetRequiredService<WithdrawBroadcastManager>();
        var uowManager = scopedServices.GetRequiredService<IUnitOfWorkManager>();
        var settings = scopedServices.GetRequiredService<ISettingProvider>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await dbContextProvider.GetDbContextAsync();
        var network = await WithdrawGuard.NetworkQuery(dbContext).AsNoTracking().FirstOrDefaultAsync(ct);
        if (network == null)
        {
            return;
        }

        // 提现与扫链共用网关，无哈希恢复会通过按地址补查寻找原交易。
        var gateway = await CreateGatewayAsync(factory, dbContext, network, Logger);
        await uow.CompleteAsync();
        if (gateway == null)
        {
            return;
        }

        var autoApproveBatch = await settings.GetIntAsync(
            CollectionSettings.Withdraw.AutoApproveBatchSize, WithdrawDefaults.AutoApproveBatchSize);
        var broadcastBatch = await settings.GetIntAsync(
            CollectionSettings.Withdraw.BroadcastBatchSize, WithdrawDefaults.BroadcastBatchSize);
        await manager.RunRoundAsync(network, gateway, autoApproveBatch, broadcastBatch, ct);
    }
}
