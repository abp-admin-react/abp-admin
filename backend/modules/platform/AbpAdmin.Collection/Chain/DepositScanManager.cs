using System;
using System.Collections.Generic;
using System.Linq;
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
    public virtual async Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway,
        int confirmations, int blockRange, int scanBehindBlocks,
        TimeSpan skipRatioWindow, decimal skipRatioThreshold,
        CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        // 在当前工作单元重读网络和代币，确保游标和心跳可以保存。
        network = await dbContext.Networks.SingleAsync(n => n.Id == network.Id, ct);
        token = await dbContext.Tokens.SingleAsync(t => t.Id == token.Id, ct);

        var head = await gateway.GetLatestBlockNumberAsync(ct);
        network.LatestChainHead = head;

        // 后续归集用地址的派生索引签名，匹配日志时保留该索引。
        var watched = await dbContext.DepositAddresses.AsNoTracking()
            .Where(a => a.NetworkId == network.Id)
            .Select(a => new { a.Id, a.Address, a.UserId, a.AddressIndex })
            .ToListAsync(ct);
        var byAddress = watched.ToDictionary(
            a => a.Address.ToLowerInvariant(),
            a => (a.Id, a.UserId, a.AddressIndex));

        // ① 首次打开：游标 = 链头 - 确认数
        var cursor = token.LastScannedBlock ?? head - confirmations;
        var target = Math.Min(head - confirmations, cursor + blockRange);

        if (target > cursor)
        {
            await ScanRangeAsync(network, token, gateway, dbContext, byAddress,
                cursor + 1, target, ct);
        }
        else if (token.LastScannedBlock == null)
        {
            // 首轮只保存起始游标，即使尚无充值地址也不回扫历史区块。
            token.LastScannedBlock = target;
        }

        // 确认推进：块龄够的 PendingConfirm → Confirmed → 并入（或新建）Queued 归集任务
        await ConfirmPendingAsync(network, token, dbContext, head, confirmations, ct);

        // 游标前进判据在 ScanRangeAsync 内已把完整读到的末端写回 token.LastScannedBlock；
        // 这里只做心跳、落后告警与跳过比例
        token.LastScanAt = now;
        network.ScanHeartbeatAt = now;

        var behind = head - (token.LastScannedBlock ?? cursor);
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

    /// <summary>扫 [from, to]；未读完就缩区间重试，缩到 1 块仍不行走按地址兜底（告警一次）。</summary>
    private async Task ScanRangeAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        Dictionary<string, (Guid Id, Guid UserId, int AddressIndex)> byAddress,
        long from, long to, CancellationToken ct)
    {
        var range = to - from + 1;
        IReadOnlyList<TokenTransferLog> logs;
        bool complete;
        long end;

        while (true)
        {
            end = from + range - 1;
            (logs, complete) = await gateway.GetTransferLogsAsync(token.ContractAddress, from, end, ct);
            if (complete && logs.All(log => IsInRange(log, from, end)))
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

        // 匹配看管中的地址并建进款单（ON CONFLICT DO NOTHING：重启不重复建单）
        var matched = 0;
        foreach (var log in logs)
        {
            if (byAddress.TryGetValue(log.To.ToLowerInvariant(), out var address))
            {
                await UpsertDepositOrderAsync(dbContext, network, token, address.Id, address.UserId, log, ct);
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
                    if (!IsInRange(log, from, to))
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

    private static bool IsInRange(TokenTransferLog log, long from, long to) =>
        log.BlockNumber >= from && log.BlockNumber <= to;

    private async Task UpsertDepositOrderAsync(
        CollectionDbContext dbContext, Entities.ColNetwork network, ColToken token,
        Guid addressId, Guid userId, TokenTransferLog log, CancellationToken ct)
    {
        var now = _clock.Now;
        var amount = TryScaleRaw(log.RawAmount, token.Decimals, out var overflow);

        // 手续费/净额快照（对齐线上 SnapshotFee：费率取网络行、向零 8 位；费率 0 = 不收费）
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
            // 手续费吃光毛额：终态不归集不加余额（快照留痕，等价线上 actual<=0 拒绝）
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
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColDepositOrders" ("Id", "NetworkId", "TokenId", "UserId", "AddressId", "ToAddress", "FromAddress", "TxHash", "LogIndex", "RawAmount", "Amount", "Fee", "ActualAmount", "FeeRateSnapshot", "BlockNumber", "BlockTime", "Status", "SweepTaskId", "CreationTime")
            VALUES ({_guidGenerator.Create()}, {network.Id}, {token.Id}, {userId}, {addressId}, {log.To}, {log.From}, {txHash}, {log.LogIndex}, {log.RawAmount}, {finalAmount}, {finalFee}, {finalActual}, {finalFeeRate}, {log.BlockNumber}, {now}, {(int)status}, NULL, {now})
            ON CONFLICT ("TxHash", "LogIndex") DO NOTHING
            """, ct);

        if (inserted > 0 && status == DepositOrderStatus.PrecisionOverflow)
        {
            // 超精度进款要告警（告警清单；冷却靠「同哈希只建一次单」天然节流）
            await _alerts.RaiseAsync(null, "精度放不下的转入",
                $"代币 {token.Symbol} 进款 {log.RawAmount} 超出 numeric(28,18)，已记进款单（{txHash}#{log.LogIndex}）。");
        }
    }

    /// <summary>确认成熟进款，并在地址锁内挂队。批次截尾后的人工放行单保留入队资格。</summary>
    private async Task ConfirmPendingAsync(
        ColNetwork network, ColToken token, CollectionDbContext dbContext,
        long head, int confirmations, CancellationToken ct)
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
            foreach (var order in orders)
            {
                order.Status = DepositOrderStatus.Confirmed;
            }

            var automatic = orders.Where(o => o.ManuallyReleased || o.Amount <= autoMax).ToList();
            await DepositSweepQueue.EnqueueAsync(queueDb, automatic, () => _guidGenerator.Create(), _clock.Now, ct);
            await queueUow.CompleteAsync(ct);
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
            await _alerts.RaiseAsync(null, "低于最低额跳过比例异常",
                $"代币 {token.Symbol} 最近 {window.TotalMinutes:0} 分钟进款 {total} 笔中跳过 {skipped} 笔（{ratio:p0} > 阈值 {threshold:p0}）。");
            // 进程内冷却（多实例各有一份，重复告警有限）：窗口 24h，条件常真时每 30 分钟提醒一次
            SkipRatioAlertedAt[token.Id] = now;
        }
    }

    /// <summary>原始整数（token 单位）→ decimal；超出 numeric(28,18)（整数部分 > 10 位）判溢出。</summary>
    internal static decimal? TryScaleRaw(string raw, int decimals, out bool overflow)
    {
        overflow = false;
        var digits = raw.TrimStart('0');
        if (digits.Length == 0)
        {
            return 0m;
        }

        // numeric(28,18)：小数 18 位 + 整数至多 10 位 → 原始整数至多 28 位
        if (raw.TrimStart('0').Length > 28)
        {
            overflow = true;
            return null;
        }

        if (decimals == 0)
        {
            return decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }

        var padded = digits.PadLeft(decimals + 1, '0');
        var text = padded[..^decimals] + "." + padded[^decimals..];
        return decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }
}
