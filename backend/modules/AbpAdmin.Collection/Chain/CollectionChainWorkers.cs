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
/// 扫链 worker：逐代币跑 DepositScanManager（一币一游标，一币失败不挡另一币）。轮末推 SignalR 进度。
/// 开关/间隔/扫链参数每轮热读设置（Collection.Chain.*，SettingUi 免重启）。
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
            TimeSpan.FromSeconds(await settings.GetIntAsync(
                CollectionSettings.Chain.ScanIntervalSeconds, ChainDefaults.ScanIntervalSeconds)));
    }

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContext = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var scanManager = scopedServices.GetRequiredService<DepositScanManager>();
        var pusher = scopedServices.GetService<Contracts.IChainPushNotifier>();
        var uowManager = scopedServices.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
        var settings = scopedServices.GetRequiredService<ISettingProvider>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContext.GetDbContextAsync();
        var network = await db.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
        if (network == null) return;

        var gateway = await CreateGatewayAsync(factory, db, network, Logger);
        if (gateway == null) return;

        var tokens = await db.Tokens
            .Where(t => t.NetworkId == network.Id)
            .ToListAsync(ct); // 含禁用的（进度推送需要全量）
        await uow.CompleteAsync();

        var enabledTokens = tokens.Where(t => t.IsEnabled).ToList();
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
                    network, token, gateway,
                    confirmations, blockRange, scanBehindBlocks,
                    skipWindow, skipThreshold,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "扫链 {Symbol} 一轮失败，跳过该币本轮", token.Symbol);
            }
        }

        // 轮末推 SignalR 进度（全部代币通道的最新状态）
        if (pusher != null)
        {
            try
            {
                var head = network.LatestChainHead ?? 0;
                foreach (var t in tokens)
                {
                    await pusher.PushScanProgressAsync(new Contracts.ChainScanProgressEto
                    {
                        Symbol = t.Symbol,
                        LastScannedBlock = t.LastScannedBlock,
                        LatestChainHead = network.LatestChainHead,
                        BlocksBehind = t.LastScannedBlock.HasValue && head > 0 ? head - t.LastScannedBlock.Value : null,
                        LastScanAt = t.LastScanAt,
                        IsEnabled = t.IsEnabled,
                    });
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
/// 归集 worker：逐代币跑 SweepManager。轮末推 SignalR 任务变更快照。
/// 开关/间隔/归集参数每轮热读设置（Collection.Chain.*，SettingUi 免重启）。
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
        var sweepManager = scopedServices.GetRequiredService<SweepManager>().WithMasterKey(factory.MasterKey);
        var pusher = scopedServices.GetService<Contracts.IChainPushNotifier>();
        var uowManager = scopedServices.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
        var settings = scopedServices.GetRequiredService<ISettingProvider>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContext.GetDbContextAsync();
        var network = await db.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
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
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "归集 {Symbol} 一轮失败，跳过该币本轮", token.Symbol);
            }
        }

        // 轮末推 SignalR 任务状态（活跃任务的最新快照）
        if (pusher != null)
        {
            try
            {
                using var pushUow = uowManager.Begin(requiresNew: true, isTransactional: false);
                var pushDb = await dbContext.GetDbContextAsync();
                var symbols = await pushDb.Tokens.ToDictionaryAsync(t => t.Id, t => t.Symbol, ct);
                var activeTasks = await pushDb.SweepTasks.AsNoTracking()
                    .Where(t => t.Status != Entities.SweepTaskStatus.Swept)
                    .OrderByDescending(t => t.StatusChangedAt)
                    .Take(20)
                    .ToListAsync(ct);
                await pushUow.CompleteAsync();

                foreach (var task in activeTasks)
                {
                    await pusher.PushSweepChangedAsync(new Contracts.ChainSweepChangedEto
                    {
                        SweepTaskId = task.Id,
                        Symbol = symbols.TryGetValue(task.TokenId, out var s) ? s : "?",
                        UserId = task.UserId,
                        Status = task.Status.ToString(),
                        Amount = Contracts.CollectionAmounts.Format(task.Amount),
                        StatusChangedAt = task.StatusChangedAt,
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "SignalR 任务推送失败（不影响归集）");
            }
        }
    }
}
