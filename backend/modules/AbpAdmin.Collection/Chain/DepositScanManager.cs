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
/// 扫链一轮（计划第 5.6 节游标纪律，一币一游标）：
/// ① 首次打开 = 链头 - 确认数，不从创世块扫；② 游标只在区间确认读完（未触上限、无节点报错）后前进；
/// ③ 缩到 1 块仍读不完 → 按地址分批兜底并告警。（「连续多轮低位后区间放回」未实现——当前区间固定为配置值。）
/// 低于 MinDeposit / 放不进 numeric(28,18) 的转入记终态进款单，游标照常前进，不归集不加余额；
/// 首次打开游标落位到链头-确认数（历史转入按计划口径不补扫）；落后超阈值告警（冷却看网络行）。
/// </summary>
public class DepositScanManager : DomainService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ChainAlertService _alerts;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly Volo.Abp.Guids.IGuidGenerator _guidGenerator;
    private readonly Contracts.IChainPushNotifier? _pushNotifier;

    // 进程内告警冷却（static：管理器是 transient，每轮新实例——实例字典冷却永不跨轮生效，
    // 评审 M 级。多实例各有一份，重复告警有限度可接受）
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

        // network/token 由 worker 在自己的工作单元加载——跨工作单元是分离实体，直接改会被静默丢弃
        //（E2E 抓到：游标/心跳从未落库）。管理器在本工作单元重读，参数仅当身份/配置用。
        network = await dbContext.Networks.SingleAsync(n => n.Id == network.Id, ct);
        token = await dbContext.Tokens.SingleAsync(t => t.Id == token.Id, ct);

        var head = await gateway.GetLatestBlockNumberAsync(ct);
        network.LatestChainHead = head;

        // 看管中的地址集（内存比对是常数时间；主成本是该合约区间内的全部 Transfer，见计划）。
        // AddressIndex 随行传递：归集任务创建时必须带上，否则签名用 index-0 错误密钥（评审 H 级）
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
            // 首轮窗口为空（游标=链头-确认数，按定义无可扫区间）：游标无条件落位到 target。
            // 此前的 watchedDbCount>0 守卫是死分支——cursor 与 target 每轮都由当前链头重算，
            // 二者恒等，有看管地址的代币永远进不了任何分支：游标永远 null、进款永不记录且无告警
            //（评审 H 级）。落位即「扫链已跑过一轮」，此前的历史转入按计划口径不补扫。
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
        var end = to;

        while (true)
        {
            (logs, complete) = await gateway.GetTransferLogsAsync(token.ContractAddress, from, from + range - 1, ct);
            if (complete)
            {
                end = from + range - 1;
                break;
            }

            if (range > 1)
            {
                // 没读完：不前进，缩小区间（计划第 5.6 节）
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
            logs = await FallbackByAddressAsync(gateway, token, byAddress.Keys.ToList(), from, from, ct);
            complete = true;
            end = from;
            break;
        }

        // 匹配看管中的地址并建进款单（ON CONFLICT DO NOTHING：重启不重复建单）
        var matched = logs.Where(l => byAddress.ContainsKey(l.To.ToLowerInvariant())).ToList();
        foreach (var log in matched)
        {
            var (addressId, userId, addressIndex) = byAddress[log.To.ToLowerInvariant()];
            await UpsertDepositOrderAsync(dbContext, network, token, addressId, userId, log, ct);
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
            token.Symbol, from, end, logs.Count, matched.Count, advanced ? "已推进" : "未推进");
    }

    /// <summary>按地址分批兜底（Etherscan V2 tokentx），聚合去重（去重仍靠进款单唯一键兜底）。</summary>
    private async Task<IReadOnlyList<TokenTransferLog>> FallbackByAddressAsync(
        IBscChainGateway gateway, ColToken token, List<string> addresses, long from, long to, CancellationToken ct)
    {
        var merged = new Dictionary<(string Tx, int Log), TokenTransferLog>();
        foreach (var address in addresses)
        {
            var page = 1;
            AddressTransferPage result;
            do
            {
                result = await gateway.GetTransfersForAddressAsync(
                    token.ContractAddress, address, from, to, page, pageSize: 200, ct);
                foreach (var log in result.Logs)
                {
                    merged[(log.TxHash, log.LogIndex)] = log;
                }

                page++;
            } while (result.Incomplete && page <= 10);
        }

        return merged.Values.ToList();
    }

    private async Task UpsertDepositOrderAsync(
        CollectionDbContext dbContext, Entities.ColNetwork network, ColToken token,
        Guid addressId, Guid userId, TokenTransferLog log, CancellationToken ct)
    {
        var now = _clock.Now;
        var amount = TryScaleRaw(log.RawAmount, token.Decimals, out var overflow);

        // 手续费/净额快照（对齐线上 SnapshotFee：费率取网络行、向零 8 位；费率 0 = 不收费）
        var (feeRate, fee, actual) = SnapshotDepositFee(network.DepositFeeRate, amount);

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

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColDepositOrders" ("Id", "NetworkId", "TokenId", "UserId", "AddressId", "ToAddress", "FromAddress", "TxHash", "LogIndex", "RawAmount", "Amount", "Fee", "ActualAmount", "FeeRateSnapshot", "BlockNumber", "BlockTime", "Status", "SweepTaskId", "CreationTime")
            VALUES ({_guidGenerator.Create()}, {network.Id}, {token.Id}, {userId}, {addressId}, {log.To}, {log.From}, {log.TxHash}, {log.LogIndex}, {log.RawAmount}, {finalAmount}, {finalFee}, {finalActual}, {finalFeeRate}, {log.BlockNumber}, {now}, {(int)status}, NULL, {now})
            ON CONFLICT ("TxHash", "LogIndex") DO NOTHING
            """, ct);


        if (status == DepositOrderStatus.PrecisionOverflow)
        {
            // 超精度进款要告警（告警清单；冷却靠「同哈希只建一次单」天然节流）
            await _alerts.RaiseAsync(null, "精度放不下的转入",
                $"代币 {token.Symbol} 进款 {log.RawAmount} 超出 numeric(28,18)，已记进款单（{log.TxHash}#{log.LogIndex}）。");
        }
    }

    private async Task ConfirmPendingAsync(
        ColNetwork network, ColToken token, CollectionDbContext dbContext,
        long head, int confirmations, CancellationToken ct)
    {
        var now = _clock.Now;
        var pending = await dbContext.DepositOrders
            .Where(o => o.TokenId == token.Id
                        && ((o.Status == DepositOrderStatus.PendingConfirm
                             && o.BlockNumber <= head - confirmations)
                            || (o.Status == DepositOrderStatus.Confirmed && o.SweepTaskId == null)))
            .OrderBy(o => o.BlockNumber)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return;
        }

        // 本轮 Queued 任务一次载入（AddressId → 任务）：逐单查询是 N+1，
        // 且 (AddressId, TokenId) 的部分唯一索引过滤 Status IN (1,2) 不覆盖 Queued
        var addressIds = pending.Select(o => o.AddressId).Distinct().ToList();
        var queuedByAddress = await dbContext.SweepTasks
            .Where(t => t.TokenId == token.Id && t.Status == SweepTaskStatus.Queued
                        && addressIds.Contains(t.AddressId))
            .OrderBy(t => t.CreationTime)
            .ToListAsync(ct);
        // 部分唯一索引不覆盖 Queued：历史/手工数据可能出现同址多任务——
        // 取最旧的一条并入，其余本轮回避（绝不因字典键冲突打断整个通道）
        var queuedByAddressDict = queuedByAddress
            .GroupBy(t => t.AddressId)
            .ToDictionary(g => g.Key, g => g.First());

        // 地址行补载（AddressIndex 是归集签名的必需品，评审 H 级：缺它=用 index-0 错误密钥签名）
        var addressIndexes = await dbContext.DepositAddresses
            .Where(a => addressIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (int?)a.AddressIndex, ct);

        foreach (var order in pending)
        {
            if (order.Status == DepositOrderStatus.PendingConfirm)
            {
                order.Status = DepositOrderStatus.Confirmed;
            }

            // 大额放行门（线上 AutoCreditMaxAmount 同款）：毛额超过自动额度的单停在 Confirmed
            // 不挂归集任务，等管理端放行（放行只入队，入账仍走归集确认的净额路径）。
            // 复扫谓词会每轮重选这些单——每轮重判一次门槛，放行前幂等跳过
            var gross = order.Amount ?? 0m;
            var autoMax = network.AutoCreditMaxAmount;
            if (autoMax >= 0 && gross > autoMax)
            {
                Logger.LogInformation(
                    "进款单 {OrderId} 毛额 {Gross} 超过自动归集额度 {Max}，停在 Confirmed 等管理端放行",
                    order.Id, gross, autoMax);
                continue;
            }

            // 已 Confirmed 的解链尾巴单：保持状态，仅重新链接到（新）Queued 任务

            if (!queuedByAddressDict.TryGetValue(order.AddressId, out var task))
            {
                task = new ColSweepTask(_guidGenerator.Create())
                {
                    NetworkId = network.Id,
                    TokenId = token.Id,
                    UserId = order.UserId,
                    AddressId = order.AddressId,
                    Address = order.ToAddress,
                    // 归集签名的 HD 索引：缺省 0 是未用地址，签出来的是错误密钥（评审 H 级）
                    AddressIndex = addressIndexes.TryGetValue(order.AddressId, out var idx) ? idx ?? 0 : 0,
                    Amount = 0m, // 激活时按已链接进款单求和（每轮取出上限在归集侧）
                    Status = SweepTaskStatus.Queued,
                    StatusChangedAt = now,
                    CreationTime = now,
                };
                await dbContext.SweepTasks.AddAsync(task, ct);
                await dbContext.SaveChangesAsync(ct); // 同轮后续单要能查到（合并语义）
                queuedByAddressDict[order.AddressId] = task;
            }

            order.SweepTaskId = task.Id;
        }

        Logger.LogInformation("代币 {Symbol} 确认进款 {Count} 笔（待归集）", token.Symbol, pending.Count);
    }

    /// <summary>
    /// 充值手续费快照（对齐线上 SnapshotFee）：fee = truncate8(毛额 × 费率)，actual = 毛额 − fee。
    /// 费率 0 = 不收费（fee=0、actual=毛额）。overflow 时调用方已走终态，不入此函数语义。
    /// </summary>
    internal static (decimal FeeRate, decimal Fee, decimal Actual) SnapshotDepositFee(decimal feeRate, decimal? gross)
    {
        if (gross is not { } value)
        {
            return (feeRate, 0m, 0m);
        }

        var fee = feeRate <= 0 ? 0m : decimal.Truncate(value * feeRate * 100_000_000m) / 100_000_000m;
        return (feeRate, fee, value - fee);
    }

    private async Task CheckSkipRatioAsync(
        ColToken token, CollectionDbContext dbContext, DateTime now,
        TimeSpan window, decimal threshold, CancellationToken ct)
    {
        if (threshold <= 0)
        {
            return;
        }

        var since = now - window;
        // 单次条件聚合（原先 COUNT×2 每轮全扫）
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
