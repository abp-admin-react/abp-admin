using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Settings;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 按代币推进充值游标。每轮读取当前扫链设置，单个代币失败时继续扫描其他代币。
/// </summary>
public class DepositScanWorker : CollectionChainWorkerBase
{
    public DepositScanWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        CollectionChainWorkerOptions options,
        ILogger<DepositScanWorker> logger)
        : base(distributedLock, scopeFactory, configuration,
            TimeSpan.FromSeconds(ChainDefaults.ScanIntervalSeconds),
            TimeSpan.FromSeconds(options.ChainRoundBudgetSeconds),
            logger)
    {
    }

    protected override string LockKey => CollectionConsts.ScannerLockKey;

    protected override async Task<WorkerRoundPlan> GetRoundPlanAsync(IServiceProvider scopedServices)
    {
        var settings = scopedServices.GetRequiredService<ISettingProvider>();
        return WorkerRoundPlan.Create(
            await settings.IsTrueAsync(CollectionSettings.Chain.ScanEnabled),
            TimeSpan.FromSeconds(await settings.GetScanIntervalSecondsAsync()));
    }

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContext = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<BscDepositScanProviderFactory>();
        var scanManager = scopedServices.GetRequiredService<DepositScanManager>();
        var pusher = scopedServices.GetService<Contracts.IChainPushNotifier>();
        var uowManager = scopedServices.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
        var settings = scopedServices.GetRequiredService<ISettingProvider>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContext.GetDbContextAsync();
        var network = await db.FindBscMainnetAsync(ct);
        if (network == null) return;

        var providers = await factory.CreateAsync(db, network, ct);

        var enabledTokens = await db.Tokens
            .Where(t => t.NetworkId == network.Id && t.IsEnabled)
            .ToListAsync(ct);
        await uow.CompleteAsync();

        var confirmations = await settings.GetIntAsync(
            CollectionSettings.Chain.Confirmations, ChainDefaults.Confirmations);
        var blockRange = await settings.GetIntAsync(
            CollectionSettings.Chain.BlockRange, ChainDefaults.BlockRange);
        var scanBehindBlocks = await settings.GetIntAsync(
            CollectionSettings.Chain.ScanBehindBlocks, ChainDefaults.ScanBehindBlocks);
        var skipWindow = TimeSpan.FromMinutes(await settings.GetIntAsync(
            CollectionSettings.Chain.SkipRatioWindowMinutes, ChainDefaults.SkipRatioWindowMinutes));
        var skipThreshold = await settings.GetDecimalAsync(
            CollectionSettings.Chain.SkipRatioThreshold, ChainDefaults.SkipRatioThreshold);

        foreach (var token in enabledTokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await scanManager.RunTokenRoundAsync(
                    network, token, providers,
                    confirmations, blockRange, scanBehindBlocks,
                    skipWindow, skipThreshold,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "扫链 {Symbol} 一轮失败，跳过该币本轮", token.Symbol);
            }
        }

        // 从数据库一次读取全部代币的已提交进度，包含禁用代币和本轮失败的代币。
        if (pusher != null)
        {
            try
            {
                using var read = uowManager.Begin(requiresNew: true, isTransactional: false);
                var committed = await dbContext.GetDbContextAsync();
                var progress = await (from token in committed.Tokens
                    join chain in committed.Networks on token.NetworkId equals chain.Id
                    where chain.Id == network.Id
                    select new Contracts.ChainScanProgressEto
                    {
                        Symbol = token.Symbol, LastScannedBlock = token.LastScannedBlock,
                        LatestChainHead = chain.LatestChainHead,
                        BlocksBehind = chain.LatestChainHead > 0 ? chain.LatestChainHead - token.LastScannedBlock : null,
                        LastScanAt = token.LastScanAt, IsEnabled = token.IsEnabled
                    }).ToListAsync(ct);
                await read.CompleteAsync(ct);
                foreach (var item in progress)
                {
                    await pusher.PushScanProgressAsync(item);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "SignalR 进度推送失败（不影响扫链）");
            }
        }
    }
}

/// <summary>
/// 按代币执行归集轮次。归集管理器按已提交的变化通知运营端。
/// </summary>
public class SweepWorker : CollectionChainWorkerBase
{
    public SweepWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        CollectionChainWorkerOptions options,
        ILogger<SweepWorker> logger)
        : base(distributedLock, scopeFactory, configuration,
            TimeSpan.FromSeconds(ChainDefaults.SweepIntervalSeconds),
            TimeSpan.FromSeconds(options.ChainRoundBudgetSeconds),
            logger)
    {
    }

    protected override string LockKey => CollectionConsts.SweepLockKey;

    protected override async Task<WorkerRoundPlan> GetRoundPlanAsync(IServiceProvider scopedServices)
    {
        var settings = scopedServices.GetRequiredService<ISettingProvider>();
        return WorkerRoundPlan.Create(
            await settings.IsTrueAsync(CollectionSettings.Chain.SweepEnabled),
            TimeSpan.FromSeconds(await settings.GetIntAsync(
                CollectionSettings.Chain.SweepIntervalSeconds, ChainDefaults.SweepIntervalSeconds)));
    }

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContext = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var sweepManager = scopedServices.GetRequiredService<SweepManager>().WithMasterKey(await factory.GetMasterKeyAsync());
        var uowManager = scopedServices.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
        var settings = scopedServices.GetRequiredService<ISettingProvider>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContext.GetDbContextAsync();
        var network = await db.FindBscMainnetAsync(ct);
        if (network == null) return;

        var tokens = await db.Tokens
            .Where(t => t.NetworkId == network.Id && t.IsEnabled)
            .ToListAsync(ct);

        // 网关在工作单元内组装（key 池查询/LastUsedAt 回写要活的上下文）
        var gateway = await CreateGatewayAsync(factory, db, network, Logger);
        if (gateway == null) return;
        await uow.CompleteAsync();

        var confirmations = await settings.GetIntAsync(
            CollectionSettings.Chain.Confirmations, ChainDefaults.Confirmations);
        var roundLimit = await settings.GetOptionalPositiveDecimalAsync(CollectionSettings.Chain.SweepRoundLimit);
        var batchSize = await settings.GetIntAsync(CollectionSettings.Chain.SweepTaskBatchSize, ChainDefaults.SweepTaskBatchSize);
        var fuelBnbLimit = await settings.GetDecimalAsync(
            CollectionSettings.Chain.FuelBnbLimit, ChainDefaults.FuelBnbLimit);
        var retryCooldown = TimeSpan.FromMinutes(await settings.GetIntAsync(
            CollectionSettings.Chain.RetryCooldownMinutes, ChainDefaults.RetryCooldownMinutes));
        var rebroadcastGrace = TimeSpan.FromMinutes(await settings.GetIntAsync(
            CollectionSettings.Chain.RebroadcastGraceMinutes, ChainDefaults.RebroadcastGraceMinutes));
        var sweepStuck = TimeSpan.FromMinutes(await settings.GetIntAsync(
            CollectionSettings.Chain.SweepStuckMinutes, ChainDefaults.SweepStuckMinutes));
        var gasAlertThreshold = await settings.GetDecimalAsync(
            CollectionSettings.Chain.GasBnbAlertThreshold, ChainDefaults.GasBnbAlertThreshold);

        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await sweepManager.RunTokenRoundAsync(
                    network, token, gateway,
                    confirmations, roundLimit, fuelBnbLimit,
                    retryCooldown,
                    rebroadcastGrace,
                    sweepStuck,
                    gasAlertThreshold,
                    ct, batchSize);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "归集 {Symbol} 一轮失败，跳过该币本轮", token.Symbol);
            }
        }

    }
}
