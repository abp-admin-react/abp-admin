using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
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
/// 提现广播 worker（M3 薄壳）：轮次逻辑在 <see cref="WithdrawBroadcastManager"/>（可注入假网关直测）。
/// 开关 Collection.Withdraw.Enabled（设置页热改，默认 false——纯睡眠不取锁不碰链；与提交总闸同键同源）；
/// 广播节奏（间隔/批量）同样热读。Redis 轮锁 + 墙钟预算与扫链/归集 worker 同纪律（CollectionChainWorkerBase）。
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
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
        if (network == null)
        {
            return;
        }

        // 广播只需要 eth_* 调用（nonce/余额/签名/回执），不扫日志——但沿用扫链的网关组装纪律
        //（key 池优先、RpcCipher 回退、cap 未测量拒绝工作），运维只需维护一套 RPC 配置
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
