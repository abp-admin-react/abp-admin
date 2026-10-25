using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 热钱包逐笔监控 worker（薄壳）：轮逻辑在 <see cref="HotWalletWatchManager"/>（可注入假网关直测）。
/// 开关/间隔热读设置（Collection.Chain.HotWalletWatch*，默认关/300s）；只读监控不碰签名面。
/// </summary>
public class HotWalletWatchWorker : CollectionChainWorkerBase
{
    public HotWalletWatchWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        CollectionChainWorkerOptions options,
        ILogger<HotWalletWatchWorker> logger)
        : base(distributedLock, scopeFactory, configuration,
            TimeSpan.FromSeconds(ChainDefaults.HotWalletWatchIntervalSeconds),
            TimeSpan.FromSeconds(options.ChainRoundBudgetSeconds),
            logger)
    {
    }

    protected override string LockKey => CollectionConsts.HotWalletWatchLockKey;

    /// <summary>测试桥：宿主注册测试断言轮锁键（internal + InternalsVisibleTo）。</summary>
    internal string LockKeyExposeForTest() => LockKey;

    protected override async Task<WorkerRoundPlan> GetRoundPlanAsync(IServiceProvider scopedServices)
    {
        var settings = scopedServices.GetRequiredService<ISettingProvider>();
        return WorkerRoundPlan.Create(
            await settings.IsTrueAsync(CollectionSettings.Chain.HotWalletWatchEnabled),
            TimeSpan.FromSeconds(await settings.GetIntAsync(
                CollectionSettings.Chain.HotWalletWatchIntervalSeconds,
                ChainDefaults.HotWalletWatchIntervalSeconds)));
    }

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContextProvider = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var manager = scopedServices.GetRequiredService<HotWalletWatchManager>();
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

        var gateway = await CreateGatewayAsync(factory, dbContext, network, Logger);
        await uow.CompleteAsync();
        if (gateway == null)
        {
            return;
        }

        var confirmations = await settings.GetIntAsync(
            CollectionSettings.Chain.Confirmations, ChainDefaults.Confirmations);
        await manager.RunRoundAsync(network, gateway, confirmations, ct);
    }
}
