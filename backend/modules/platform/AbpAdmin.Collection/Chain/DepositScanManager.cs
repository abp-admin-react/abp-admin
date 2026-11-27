using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 每个代币维护自己的游标，初始位置为链头减确认数。完整读取区间后才推进，
/// 单个区块仍读不全时按地址补查。低于最低金额或超出存储精度的进款记录为终态，不参与归集入账。
/// </summary>
public class DepositScanManager : DomainService
{
    private const int AddressFallbackPageSize = 200;
    private const int AddressFallbackMaxPages = 10;

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ChainAlertService _alerts;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly Volo.Abp.Guids.IGuidGenerator _guidGenerator;
    private readonly Contracts.IChainPushNotifier? _pushNotifier;

    // 管理器按轮创建，告警冷却存储在进程内，供各轮共用。
    private static readonly Dictionary<Guid, DateTime> SkipRatioAlertedAt = new();
    private static readonly Dictionary<Guid, DateTime> FallbackAlertedAt = new();
    private static readonly ConcurrentDictionary<Guid, DateTime> AllProvidersFailedAlertedAt = new();

    public DepositScanManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ChainAlertService alerts,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        Volo.Abp.Guids.IGuidGenerator guidGenerator,
        System.IServiceProvider? serviceProvider = null)
    {
        _dbContextProvider = dbContextProvider;
        _alerts = alerts;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _pushNotifier = serviceProvider?.GetService(typeof(Contracts.IChainPushNotifier)) as Contracts.IChainPushNotifier;
    }

    /// <summary>扫一个代币通道；一种币连续失败不挡住另一种（调用方逐币 try/catch）。</summary>
    public virtual Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway,
        int confirmations, int blockRange, int scanBehindBlocks,
        TimeSpan skipRatioWindow, decimal skipRatioThreshold,
        CancellationToken ct) => RunTokenRoundCoreAsync(network, token, gateway, null, confirmations,
            blockRange, scanBehindBlocks, skipRatioWindow, skipRatioThreshold, ct);

    internal Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, IReadOnlyList<DepositScanProvider> providers,
        int confirmations, int blockRange, int scanBehindBlocks,
        TimeSpan skipRatioWindow, decimal skipRatioThreshold,
        CancellationToken ct) => RunTokenRoundCoreAsync(network, token, null, providers, confirmations,
            blockRange, scanBehindBlocks, skipRatioWindow, skipRatioThreshold, ct);

    private async Task RunTokenRoundCoreAsync(
        ColNetwork network, ColToken token, IBscChainGateway? legacyGateway, IReadOnlyList<DepositScanProvider>? providers,
        int confirmations, int blockRange, int scanBehindBlocks,
        TimeSpan skipRatioWindow, decimal skipRatioThreshold, CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        // 在当前工作单元重读网络和代币，确保游标和心跳可以保存。
        network = await dbContext.Networks.SingleAsync(n => n.Id == network.Id, ct);
        token = await dbContext.Tokens.SingleAsync(t => t.Id == token.Id, ct);
        await using var changes = new ChainRecordChanges(dbContext, token.Symbol, _pushNotifier, Logger);
        if (token.Decimals is < 0 or > byte.MaxValue)
        {
            throw new InvalidOperationException($"代币 {token.Symbol} 的小数位无效：{token.Decimals}");
        }

        if (network.LegacyAddressIndexHighWaterMark.HasValue && !token.LastScannedBlock.HasValue)
        {
            Logger.LogWarning("旧钱包迁移后须先交接扫链游标，代币 {Symbol} 本轮跳过", token.Symbol);
            return;
        }

        // 后续归集用地址的派生索引签名，匹配日志时保留该索引。
        var watched = await dbContext.DepositAddresses.AsNoTracking()
            .Where(a => a.NetworkId == network.Id && !a.IsRetired)
            .Select(a => new { a.Id, a.Address, a.UserId, a.AddressIndex })
            .ToListAsync(ct);
        var byAddress = watched.ToDictionary(
            a => a.Address,
            a => (a.Id, a.UserId, a.AddressIndex), StringComparer.OrdinalIgnoreCase);

        long head;
        if (legacyGateway != null)
        {
            head = await legacyGateway.GetLatestBlockNumberAsync(ct);
            var cursor = token.LastScannedBlock ?? head - confirmations;
            var target = Math.Min(head - confirmations, cursor + blockRange);
            if (target > cursor)
            {
                await ScanRangeAsync(network, token, legacyGateway, dbContext, byAddress, cursor + 1, target, changes, ct);
            }
            else if (token.LastScannedBlock == null)
            {
                // 首轮只保存起始游标，即使尚无充值地址也不回扫历史区块。
                token.LastScannedBlock = target;
            }
        }
        else
        {
            var result = await ScanWithProvidersAsync(network, token, providers!, dbContext, byAddress, confirmations,
                blockRange, changes, ct);
            if (!result.HasValue)
            {
                await dbContext.SaveChangesAsync(ct);
                await uow.CompleteAsync(ct);
                return;
            }
            head = result.Value;
        }
        network.LatestChainHead = head;

        // 确认推进：块龄够的 PendingConfirm → Confirmed → 并入（或新建）Queued 归集任务
        await ConfirmPendingAsync(network, token, dbContext, head, confirmations, changes, ct);

        // 游标前进判据在 ScanRangeAsync 内已把完整读到的末端写回 token.LastScannedBlock；
        // 这里只做心跳、落后告警与跳过比例
        token.LastScanAt = now;
        network.ScanHeartbeatAt = now;

        var behind = head - (token.LastScannedBlock ?? (head - confirmations));
        if (behind > scanBehindBlocks
            && ChainAlertService.ShouldAlert(network.ScanBehindAlertedAt, TimeSpan.FromMinutes(30), now))
        {
            await _alerts.RaiseAsync(null, "扫链落后过深",
                $"代币 {token.Symbol} 游标落后链头 {behind} 块（阈值 {scanBehindBlocks}）。");
            network.ScanBehindAlertedAt = now;
        }

        await CheckSkipRatioAsync(token, dbContext, now, skipRatioWindow, skipRatioThreshold, ct);

        await dbContext.SaveChangesAsync(ct);
        await uow.CompleteAsync();
    }

    private async Task<long?> ScanWithProvidersAsync(ColNetwork network, ColToken token,
        IReadOnlyList<DepositScanProvider> providers, CollectionDbContext dbContext,
        Dictionary<string, (Guid Id, Guid UserId, int AddressIndex)> byAddress, int confirmations, int blockRange,
        ChainRecordChanges changes, CancellationToken ct)
    {
        var watched = byAddress.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? failure = null;
        foreach (var provider in providers)
        {
            var now = _clock.Now;
            if (provider.Health.GetFailure(provider.Id, now) is { } cooldown)
            {
                failure ??= cooldown;
                continue;
            }
            if (provider.Source is not { } source)
            {
                failure ??= DepositScanFailure.InvalidConfiguration;
                continue;
            }

            long head;
            long cursor;
            long from;
            long end;
            IReadOnlyList<TokenTransferLog> logs;
            bool readLogs;
            var replacingLegacy = !provider.IsLegacy && !source.RequiresReconciliation
                && network.PreferManagedScanProviders && network.LegacyDepositRpcCipher != null;
            using var providerBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            providerBudget.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                head = await source.GetHeadAsync(providerBudget.Token);
                var safeHead = Math.Max(0, head - confirmations);
                cursor = token.LastScannedBlock ?? safeHead;
                if (safeHead < cursor)
                {
                    throw new DepositScanReadException(DepositScanFailure.Stale);
                }
                from = cursor + 1;
                end = Math.Min(safeHead, from + blockRange - 1);
                readLogs = end >= from;
                if (!readLogs && safeHead > 0 && (replacingLegacy || failure != null
                    || token.LastScanFailure != null || token.LastScanProvider == null))
                {
                    // A head response alone does not prove that a newly configured or recovered key can read logs.
                    await ReadProviderWindowAsync(source, token.ContractAddress, watched, safeHead, safeHead,
                        providerBudget.Token);
                    readLogs = true;
                }
                (end, logs) = await ReadProviderWindowAsync(source, token.ContractAddress, watched, from, end,
                    providerBudget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                failure ??= DepositScanFailure.Timeout;
                provider.Health.MarkFailure(provider.Id, DepositScanFailure.Timeout, _clock.Now);
                continue;
            }
            catch (DepositScanReadException ex)
            {
                failure ??= ex.Code;
                provider.Health.MarkFailure(provider.Id, ex.Code, _clock.Now);
                Logger.LogWarning("扫链服务商 {ProviderId} 查询失败，代币 {Symbol}，原因 {Reason}",
                    provider.Id, token.Symbol, ex.Code);
                continue;
            }

            await RecordProviderDepositsAsync(network, token, dbContext, byAddress, logs, changes, ct);

            // Keep the legacy RPC available across failed saves, probes and scans until a managed log read succeeds.
            if (replacingLegacy && readLogs)
            {
                network.LegacyDepositRpcCipher = null;
                network.PreferManagedScanProviders = false;
            }

            if (source.RequiresReconciliation && end >= from)
            {
                token.ScanReplayFromBlock ??= from;
                token.ScanReplayToBlock = end;
            }
            token.LastScannedBlock = Math.Max(cursor, end);
            token.LastScanFailure = failure;
            if (readLogs)
            {
                token.LastScanProvider = provider.Name;
                token.LastScanProviderAt = _clock.Now;
            }

            if (token.ScanReplayFromBlock is long replayFrom && token.ScanReplayToBlock is long replayTo)
            {
                // The indexed provider may stay first indefinitely. A lower-ranked RPC can still reconcile history.
                var replaySource = source.RequiresReconciliation
                    ? providers.SkipWhile(candidate => candidate.Id != provider.Id).Skip(1)
                        .FirstOrDefault(candidate => candidate.Source is { RequiresReconciliation: false }
                            && candidate.Health.GetFailure(candidate.Id, _clock.Now) == null)?.Source
                    : source;
                if (replaySource != null)
                {
                    await ReplayProviderWindowAsync(replaySource, network, token, dbContext, byAddress, watched, replayFrom,
                        replayTo, blockRange, source.RequiresReconciliation ? confirmations : null, changes, ct);
                }
            }
            return head;
        }

        token.LastScanFailure = failure ?? DepositScanFailure.NotConfigured;
        if (providers.Count > 0)
        {
            var now = _clock.Now;
            var last = AllProvidersFailedAlertedAt.GetOrAdd(token.Id, DateTime.MinValue);
            if (ChainAlertService.ShouldAlert(last, TimeSpan.FromMinutes(30), now)
                && AllProvidersFailedAlertedAt.TryUpdate(token.Id, now, last))
            {
                await _alerts.RaiseAsync(null, "充值扫链服务商全部不可用",
                    $"代币 {token.Symbol} 的 {providers.Count} 个已启用扫链服务商均不可用，充值游标保留。错误类型：{token.LastScanFailure}。");
            }
        }
        return null;
    }

    /// <summary>Commits replay progress only after a trusted RPC returns a complete confirmed block range.</summary>
    private async Task ReplayProviderWindowAsync(IBscDepositScanSource source, ColNetwork network, ColToken token,
        CollectionDbContext dbContext, Dictionary<string, (Guid Id, Guid UserId, int AddressIndex)> byAddress,
        IReadOnlySet<string> watched, long from, long to, int blockRange, int? verifyConfirmations, ChainRecordChanges changes, CancellationToken ct)
    {
        using var replayBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        replayBudget.CancelAfter(TimeSpan.FromSeconds(8));
        long end;
        IReadOnlyList<TokenTransferLog> logs;
        try
        {
            var target = Math.Min(to, from + blockRange - 1);
            if (verifyConfirmations is int confirmations)
            {
                var head = await source.GetHeadAsync(replayBudget.Token);
                target = Math.Min(target, head - confirmations);
                if (target < from)
                {
                    return;
                }
            }
            (end, logs) = await ReadProviderWindowAsync(source, token.ContractAddress, watched, from,
                target, replayBudget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return;
        }
        catch (DepositScanReadException ex)
        {
            Logger.LogWarning("代币 {Symbol} 复核区块 {From}..{To} 尚未完成，原因 {Reason}", token.Symbol, from, to, ex.Code);
            return;
        }

        await RecordProviderDepositsAsync(network, token, dbContext, byAddress, logs, changes, ct);
        token.ScanReplayFromBlock = end < to ? end + 1 : null;
        if (token.ScanReplayFromBlock == null) token.ScanReplayToBlock = null;
    }

    private async Task RecordProviderDepositsAsync(ColNetwork network, ColToken token, CollectionDbContext dbContext,
        Dictionary<string, (Guid Id, Guid UserId, int AddressIndex)> byAddress, IReadOnlyList<TokenTransferLog> logs,
        ChainRecordChanges changes, CancellationToken ct)
    {
        foreach (var log in logs)
        {
            if (byAddress.TryGetValue(log.To, out var address))
            {
                await UpsertDepositOrderAsync(dbContext, network, token, address.Id, address.UserId, log, changes, ct);
            }
        }
    }

    private static async Task<(long End, IReadOnlyList<TokenTransferLog> Logs)> ReadProviderWindowAsync(
        IBscDepositScanSource source, string contract, IReadOnlySet<string> watched, long from, long end,
        CancellationToken ct)
    {
        while (from <= end)
        {
            try
            {
                var logs = await source.ReadLogsAsync(contract, from, end, watched, ct);
                if (logs.Any(log => !IsValidTransfer(log, from, end)))
                {
                    throw new DepositScanReadException(DepositScanFailure.EvidenceInvalid);
                }
                return (end, logs);
            }
            catch (DepositScanReadException ex) when (ex.Code == DepositScanFailure.RangeIncomplete && end > from)
            {
                end = from + (end - from - 1) / 2;
            }
        }

        return (end, []);
    }

    /// <summary>扫 [from, to]；未读完就缩区间重试，缩到 1 块仍不行走按地址兜底（告警一次）。</summary>
    private async Task ScanRangeAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        Dictionary<string, (Guid Id, Guid UserId, int AddressIndex)> byAddress,
        long from, long to, ChainRecordChanges changes, CancellationToken ct)
    {
        var range = to - from + 1;
        IReadOnlyList<TokenTransferLog> logs;
        bool complete;
        long end;

        while (true)
        {
            end = from + range - 1;
            (logs, complete) = await gateway.GetTransferLogsAsync(token.ContractAddress, from, end, ct);
            if (complete && logs.All(log => IsValidTransfer(log, from, end)))
            {
                break;
            }

            if (range > 1)
            {
                // 未读完的区间不能推进游标。
                range = Math.Max(1, range / 2);
                continue;
            }

            // 缩到 1 块仍读不完：这一块按地址分批查询并告警（进程内 30 分钟冷却防每轮刷屏）
            if (ChainAlertService.ShouldAlert(
                    FallbackAlertedAt.TryGetValue(token.Id, out var lastFb) ? lastFb : null,
                    TimeSpan.FromMinutes(30), _clock.Now))
            {
                await _alerts.RaiseAsync(null, "扫链区间按地址兜底",
                    $"代币 {token.Symbol} 块 {from} 多次读取未完整，已按地址分批查询。");
                FallbackAlertedAt[token.Id] = _clock.Now;
            }
            (logs, complete) = await FallbackByAddressAsync(gateway, token, byAddress.Keys, from, end, ct);
            break;
        }

        // 同一哈希和日志索引只建一单；重扫时仍须核对已有凭证和块高。
        var matched = 0;
        foreach (var log in logs)
        {
            if (byAddress.TryGetValue(log.To.ToLowerInvariant(), out var address))
            {
                await UpsertDepositOrderAsync(dbContext, network, token, address.Id, address.UserId, log, changes, ct);
                matched++;
            }
        }

        // 游标前进：确认读完才推进
        var advanced = false;
        if (complete)
        {
            if (token.LastScannedBlock == null || end > token.LastScannedBlock)
            {
                token.LastScannedBlock = end;
                advanced = true;
            }
        }

        Logger.LogDebug(
            "扫链 {Symbol} {From}..{End}：日志 {Logs} 条，匹配进款 {Matched}，游标{Advanced}",
            token.Symbol, from, end, logs.Count, matched, advanced ? "已推进" : "未推进");
    }

    /// <summary>按地址分批兜底。有效日志可以先落库，任一地址未读全则保留游标重试。</summary>
    private async Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> FallbackByAddressAsync(
        IBscChainGateway gateway, ColToken token, IEnumerable<string> addresses, long from, long to, CancellationToken ct)
    {
        var merged = new Dictionary<(string Tx, int Log), TokenTransferLog>();
        var complete = true;
        foreach (var address in addresses)
        {
            for (var page = 1; page <= AddressFallbackMaxPages; page++)
            {
                var result = await gateway.GetTransfersForAddressAsync(
                    token.ContractAddress, address, from, to, page, AddressFallbackPageSize, ct);
                complete &= !result.HasUnresolvedLogs;
                foreach (var log in result.Logs)
                {
                    if (!IsValidTransfer(log, from, to))
                    {
                        complete = false;
                        continue;
                    }

                    if (string.Equals(log.To, address, StringComparison.OrdinalIgnoreCase))
                    {
                        merged[(log.TxHash.ToLowerInvariant(), log.LogIndex)] = log;
                    }
                }

                if (!result.Incomplete)
                {
                    break;
                }

                if (page == AddressFallbackMaxPages)
                {
                    complete = false;
                    Logger.LogWarning("代币 {Symbol} 块 {Block} 的地址查询达到 {Pages} 页仍有后续，保留游标重试",
                        token.Symbol, from, AddressFallbackMaxPages);
                }
            }
        }

        return (merged.Values.ToList(), complete);
    }

    private static bool IsValidTransfer(TokenTransferLog log, long from, long to) =>
        log.BlockNumber >= from && log.BlockNumber <= to && IsRawTokenAmount(log.RawAmount);

    private async Task UpsertDepositOrderAsync(
        CollectionDbContext dbContext, Entities.ColNetwork network, ColToken token,
        Guid addressId, Guid userId, TokenTransferLog log, ChainRecordChanges changes, CancellationToken ct)
    {
        var now = _clock.Now;
        var amount = TryScaleRaw(log.RawAmount, token.Decimals, out var overflow);

        // 建单时固定费率和净额，供归集确认时入账。
        var (feeRate, fee, actual) = Ledger.CollectionLedgerPoster.SnapshotDepositFee(network.DepositFeeRate, amount);

        DepositOrderStatus status;
        decimal? finalAmount;
        decimal? finalFee = null;
        decimal? finalActual = null;
        decimal? finalFeeRate = null;
        if (overflow)
        {
            status = DepositOrderStatus.PrecisionOverflow;
            finalAmount = null;
        }
        else if (amount < CollectionAmounts.Parse(token.MinDeposit))
        {
            status = DepositOrderStatus.SkippedBelowMinDeposit;
            finalAmount = amount;
        }
        else if (actual <= 0)
        {
            // 净额为零的进款保留手续费快照并跳过归集。
            status = DepositOrderStatus.SkippedBelowMinDeposit;
            finalAmount = amount;
            finalFee = fee;
            finalActual = actual;
            finalFeeRate = feeRate;
        }
        else
        {
            status = DepositOrderStatus.PendingConfirm;
            finalAmount = amount;
            finalFee = fee;
            finalActual = actual;
            finalFeeRate = feeRate;
        }

        var txHash = log.TxHash.ToLowerInvariant();
        var id = _guidGenerator.Create();
        var write = dbContext.Database.SqlQuery<DepositWrite>($"""
            WITH prior AS MATERIALIZED (
                SELECT "Id", ("NetworkId" = {network.Id} AND "TokenId" = {token.Id}
                    AND lower("FromAddress") = lower({log.From}) AND lower("ToAddress") = lower({log.To})
                    AND ltrim("RawAmount", '0') = ltrim({log.RawAmount}, '0')) AS matches
                FROM "ColDepositOrders" WHERE "TxHash" = {txHash} AND "LogIndex" = {log.LogIndex}
            ), added AS (
                INSERT INTO "ColDepositOrders" ("Id", "NetworkId", "TokenId", "UserId", "AddressId", "ToAddress", "FromAddress", "TxHash", "LogIndex", "RawAmount", "Amount", "Fee", "ActualAmount", "FeeRateSnapshot", "BlockNumber", "BlockTime", "Status", "SweepTaskId", "CreationTime")
                SELECT {id}, {network.Id}, {token.Id}, {userId}, {addressId}, {log.To}, {log.From}, {txHash}, {log.LogIndex}, {log.RawAmount}, {finalAmount}, {finalFee}, {finalActual}, {finalFeeRate}, {log.BlockNumber}, {now}, {(int)status}, NULL, {now}
                WHERE NOT EXISTS (SELECT FROM prior)
                ON CONFLICT ("TxHash", "LogIndex") DO NOTHING RETURNING "Id"
            ), moved AS (
                UPDATE "ColDepositOrders" AS orders SET "BlockNumber" = {log.BlockNumber}
                FROM prior WHERE orders."Id" = prior."Id" AND prior.matches
                    AND orders."BlockNumber" < {log.BlockNumber} AND orders."Status" <> {(int)DepositOrderStatus.Swept}
                RETURNING orders."Id"
            )
            SELECT "Id", TRUE AS "Matches", TRUE AS "Changed" FROM added
            UNION ALL SELECT "Id", TRUE, TRUE FROM moved
            UNION ALL SELECT "Id", matches, FALSE FROM prior WHERE NOT EXISTS (SELECT FROM moved)
            """);
        var result = (await write.ToListAsync(ct)).SingleOrDefault();
        // A concurrent insert can win ON CONFLICT after the statement snapshot. Re-read its committed evidence.
        result ??= (await write.ToListAsync(ct)).Single();
        if (!result.Matches)
        {
            throw new InvalidOperationException($"充值日志 {txHash}#{log.LogIndex} 与已记录的链上凭证不一致，停止本轮确认，请人工核对。");
        }

        if (result.Changed) changes.DepositChanged(result.Id);
        if (result.Id == id && status == DepositOrderStatus.PrecisionOverflow)
        {
            // 超精度进款要告警（告警清单；冷却靠「同哈希只建一次单」天然节流）
            await _alerts.RaiseAsync(null, "精度放不下的转入",
                $"代币 {token.Symbol} 进款 {log.RawAmount} 超出 numeric(28,18)，已记进款单（{txHash}#{log.LogIndex}）。");
        }
    }

    private sealed class DepositWrite
    {
        public Guid Id { get; set; }
        public bool Matches { get; set; }
        public bool Changed { get; set; }
    }

    /// <summary>确认成熟进款，并在地址锁内挂队。批次截尾后的人工放行单保留入队资格。</summary>
    private async Task ConfirmPendingAsync(
        ColNetwork network, ColToken token, CollectionDbContext dbContext,
        long head, int confirmations, ChainRecordChanges changes, CancellationToken ct)
    {
        var safeHead = head - confirmations;
        var observedMax = Math.Max(0m, network.AutoCreditMaxAmount);
        var addressIds = await UnqueuedDeposits(dbContext, token.Id, safeHead)
            .Where(o => o.Status == DepositOrderStatus.PendingConfirm || o.ManuallyReleased || o.Amount <= observedMax)
            .Select(o => o.AddressId).Distinct().ToListAsync(ct);
        foreach (var addressId in addressIds)
        {
            using var queueUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var queueDb = await _dbContextProvider.GetDbContextAsync();
            await DepositSweepQueue.LockAddressAsync(queueDb, addressId, ct);
            var orders = await UnqueuedDeposits(queueDb, token.Id, safeHead)
                .Where(o => o.AddressId == addressId)
                .OrderBy(o => o.BlockNumber).ThenBy(o => o.LogIndex).ThenBy(o => o.Id)
                .ToListAsync(ct);
            if (orders.Count == 0)
            {
                continue;
            }

            var autoMax = Math.Max(0m, await queueDb.Networks.AsNoTracking()
                .Where(n => n.Id == network.Id).Select(n => n.AutoCreditMaxAmount).SingleAsync(ct));
            var newlyConfirmed = orders.Where(o => o.Status != DepositOrderStatus.Confirmed).Select(o => o.Id).ToList();
            foreach (var order in orders)
            {
                order.Status = DepositOrderStatus.Confirmed;
            }

            var automatic = orders.Where(o => o.ManuallyReleased || o.Amount <= autoMax).ToList();
            var newSweep = await DepositSweepQueue.EnqueueAsync(queueDb, automatic, () => _guidGenerator.Create(), _clock.Now, ct);
            await queueUow.CompleteAsync(ct);
            foreach (var id in newlyConfirmed.Concat(automatic.Select(o => o.Id))) changes.DepositChanged(id);
            if (newSweep is Guid sweepId) changes.SweepCreated(sweepId);
            if (automatic.Count > 0)
            {
                Logger.LogInformation("代币 {Symbol} 确认进款 {Count} 笔（待归集）", token.Symbol, automatic.Count);
            }
        }
    }

    private static IQueryable<ColDepositOrder> UnqueuedDeposits(CollectionDbContext db, Guid tokenId, long safeHead) =>
        db.DepositOrders.Where(o => o.TokenId == tokenId && o.SweepTaskId == null
            && (o.Status == DepositOrderStatus.Confirmed
                || (o.Status == DepositOrderStatus.PendingConfirm && o.BlockNumber <= safeHead)));

    private async Task CheckSkipRatioAsync(
        ColToken token, CollectionDbContext dbContext, DateTime now,
        TimeSpan window, decimal threshold, CancellationToken ct)
    {
        if (threshold <= 0)
        {
            return;
        }

        var since = now - window;
        // 单次条件聚合（避免 COUNT×2 每轮全扫）
        var counts = await dbContext.DepositOrders
            .Where(o => o.TokenId == token.Id && o.CreationTime >= since)
            .GroupBy(o => o.TokenId)
            .Select(g => new { Total = g.Count(), Skipped = g.Count(o =>
                o.Status == DepositOrderStatus.SkippedBelowMinDeposit
                || o.Status == DepositOrderStatus.PrecisionOverflow) })
            .FirstOrDefaultAsync(ct);
        if (counts == null)
        {
            return;
        }

        var total = counts.Total;
        var skipped = counts.Skipped;

        var ratio = (decimal)skipped / total;
        if (ratio > threshold && ChainAlertService.ShouldAlert(
                SkipRatioAlertedAt.TryGetValue(token.Id, out var last) ? last : null,
                TimeSpan.FromMinutes(30), now))
        {
            await _alerts.RaiseAsync(null, "充值跳过比例超过阈值",
                $"代币 {token.Symbol} 最近 {window.TotalMinutes:0} 分钟共 {total} 笔进款，其中 {skipped} 笔因低于最低额、净额为零或超出精度被跳过（{ratio:p0} > {threshold:p0}）。");
            SkipRatioAlertedAt[token.Id] = now;
        }
    }

    // ERC-20 transfer values are unsigned 256-bit integers. The original text is retained in varchar(96).
    private static bool IsRawTokenAmount(string? raw) =>
        !string.IsNullOrEmpty(raw) && raw.Length <= 96 && !raw.AsSpan().ContainsAnyExceptInRange('0', '9')
        && (raw.Length < 78 || BigInteger.Parse(raw, NumberStyles.None, CultureInfo.InvariantCulture).GetBitLength() <= 256);

    /// <summary>换算为可在 numeric(28,18) 原样存储的金额；非法输入或需要舍入时返回 null。</summary>
    internal static decimal? TryScaleRaw(string? raw, int decimals, out bool overflow)
    {
        overflow = true;
        if (!IsRawTokenAmount(raw) || decimals is < 0 or > byte.MaxValue) return null;
        var digits = raw.AsSpan().TrimStart('0');
        if (digits.Length == 0)
        {
            overflow = false;
            return 0m;
        }

        // A token may have more than 18 decimal places if the excess raw digits are all zero.
        while (decimals > CollectionAmounts.Scale && digits[^1] == '0')
        {
            digits = digits[..^1];
            decimals--;
        }
        if (decimals > CollectionAmounts.Scale || digits.Length > 10 + decimals) return null;

        overflow = false;
        var units = decimal.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        for (var i = 0; i < decimals; i++) units /= 10m;
        return units;
    }
}
