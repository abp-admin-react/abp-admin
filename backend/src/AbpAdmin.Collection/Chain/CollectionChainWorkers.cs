using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.Collection.Chain;

/// <summary>扫链 worker：逐代币跑 DepositScanManager（一币一游标，一币失败不挡另一币）。轮末推 SignalR 进度。</summary>
public class DepositScanWorker : CollectionChainWorkerBase
{
    public DepositScanWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        CollectionChainWorkerOptions options,
        ILogger<DepositScanWorker> logger)
        : base(distributedLock, scopeFactory,
            TimeSpan.FromSeconds(options.ScanIntervalSeconds),
            TimeSpan.FromSeconds(options.RoundBudgetSeconds),
            logger)
    {
        _options = options;
    }

    private readonly CollectionChainWorkerOptions _options;

    protected override string LockKey => CollectionConsts.ScannerLockKey;

    protected override bool Enabled => _options.ScanEnabled;

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContext = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var scanManager = scopedServices.GetRequiredService<DepositScanManager>();
        var pusher = scopedServices.GetService<Contracts.IChainPushNotifier>();
        var uowManager = scopedServices.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContext.GetDbContextAsync();
        var network = await db.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
        if (network == null) return;

        var gateway = CreateGateway(factory, db, network, Logger);
        if (gateway == null) return;

        var tokens = await db.Tokens
            .Where(t => t.NetworkId == network.Id)
            .ToListAsync(ct); // 含禁用的（进度推送需要全量）
        await uow.CompleteAsync();

        var enabledTokens = tokens.Where(t => t.IsEnabled).ToList();

        foreach (var token in enabledTokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await scanManager.RunTokenRoundAsync(
                    network, token, gateway,
                    _options.Confirmations, _options.BlockRange, _options.ScanBehindBlocks,
                    TimeSpan.FromMinutes(_options.SkipRatioWindowMinutes), _options.SkipRatioThreshold,
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

/// <summary>归集 worker：逐代币跑 SweepManager。轮末推 SignalR 任务变更快照。</summary>
public class SweepWorker : CollectionChainWorkerBase
{
    public SweepWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        CollectionChainWorkerOptions options,
        ILogger<SweepWorker> logger)
        : base(distributedLock, scopeFactory,
            TimeSpan.FromSeconds(options.SweepIntervalSeconds),
            TimeSpan.FromSeconds(options.RoundBudgetSeconds),
            logger)
    {
        _options = options;
    }

    private readonly CollectionChainWorkerOptions _options;

    protected override string LockKey => CollectionConsts.SweepLockKey;

    protected override bool Enabled => _options.SweepEnabled;

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContext = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var sweepManager = scopedServices.GetRequiredService<SweepManager>().WithMasterKey(factory.MasterKey);
        var pusher = scopedServices.GetService<Contracts.IChainPushNotifier>();
        var uowManager = scopedServices.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var db = await dbContext.GetDbContextAsync();
        var network = await db.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
        if (network == null) return;

        var tokens = await db.Tokens
            .Where(t => t.NetworkId == network.Id && t.IsEnabled)
            .ToListAsync(ct);
        await uow.CompleteAsync();

        var gateway = CreateGateway(factory, db, network, Logger);
        if (gateway == null) return;

        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await sweepManager.RunTokenRoundAsync(
                    network, token, gateway,
                    _options.Confirmations, _options.SweepRoundLimit, _options.FuelBnbLimit,
                    TimeSpan.FromMinutes(_options.RetryCooldownMinutes),
                    TimeSpan.FromMinutes(_options.RebroadcastGraceMinutes),
                    TimeSpan.FromMinutes(_options.SweepStuckMinutes),
                    _options.GasBnbAlertThreshold,
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
