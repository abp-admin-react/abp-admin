using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 热钱包逐笔监控（对齐线上 WithdrawHotWalletTransferWatch 一期口径）：按币扫热钱包地址的
/// ERC20 Transfer（Etherscan tokentx 通道，与哈希认领同款分页模板）——
/// 出站逐笔对账提现单 TxHash（匹配 = 正常付币），未匹配出账标异常 + [Alert:WithdrawHotWallet]
/// 告警（每轮每币聚合一条）+ SignalR 推送，管理端「确认已知」终态化；
/// 入站记为外部补币（提示性）。幂等：(TxHash, LogIndex) 唯一吸收重扫，分页返回先按 (TxHash,LogIndex)
/// 去重（页界重叠不炸唯一键）。区间读不全时缩半分窗自愈（对齐 DepositScanManager 缩半纪律）；
/// 单块仍读不全（超分页上限）→ [Alert:] 告警后跳过该块推进——监控卡死比漏一段更危险。
/// 首跑基线 = 当时安全链头（无历史基线可完整回扫；余额核对点重放留二期）。
/// 私钥不参与——只读监控，不碰签名面。
/// </summary>
public class HotWalletWatchManager : DomainService
{
    private const int PageSize = 100;
    private const int MaxPages = 20;

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ChainAlertService _alerts;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IChainPushNotifier? _pushNotifier;

    public HotWalletWatchManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ChainAlertService alerts,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator,
        IServiceProvider? serviceProvider = null)
    {
        _dbContextProvider = dbContextProvider;
        _alerts = alerts;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _pushNotifier = serviceProvider?.GetService<IChainPushNotifier>();
    }

    /// <summary>一轮：热钱包未配置整轮跳过；逐启用币跑（一币失败不挡另一币）。</summary>
    public virtual async Task RunRoundAsync(
        ColNetwork network, IBscChainGateway gateway, int confirmations, CancellationToken ct)
    {
        var walletAddress = network.WithdrawVaultAddress;
        if (string.IsNullOrEmpty(walletAddress))
        {
            return; // 未配置热钱包：无监控对象，整轮跳过（状态卡可见未配置）
        }

        var head = await gateway.GetLatestBlockNumberAsync(ct);
        var safeHead = head - Math.Max(1, confirmations);

        List<ColToken> tokens;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            tokens = await dbContext.Tokens.AsNoTracking()
                .Where(t => t.NetworkId == network.Id && t.IsEnabled)
                .ToListAsync(ct);
            await uow.CompleteAsync();
        }

        foreach (var token in tokens)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await RunTokenRoundAsync(network, token, walletAddress!, gateway, safeHead, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "热钱包监控 {Symbol} 一轮失败，跳过该币本轮", token.Symbol);
            }
        }
    }

    private async Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, string walletAddress,
        IBscChainGateway gateway, long safeHead, CancellationToken ct)
    {
        // ① 游标读取（非事务）：首跑只落基线不回扫
        ColHotWalletWatchState? state;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            state = await dbContext.HotWalletWatchStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TokenId == token.Id, ct);
            await uow.CompleteAsync();
        }

        if (state == null)
        {
            await SaveStateAsync(network, token, safeHead, lastError: null);
            Logger.LogInformation("热钱包监控 {Symbol} 首跑基线 = 安全链头 {Head}（历史区间不回扫）",
                token.Symbol, safeHead);
            return;
        }

        var fromBlock = state.LastScannedBlock.HasValue ? state.LastScannedBlock.Value + 1 : safeHead;
        // 游标不回退：确认数被热调大或 RPC 节点短暂落后会使 safeHead 低于已扫值——
        // 回写更小值会触发等于回退深度的整段重扫（幂等但白耗 Etherscan 配额）
        var effectiveHead = Math.Max(safeHead, state.LastScannedBlock ?? safeHead);
        var skippedBlocks = new List<long>(); // 跳块留痕（聚合进轮末 LastError 与告警）
        var roundAnomalies = new List<ColHotWalletTxn>(); // 轮级聚合：告警/推送每轮每币一条（防刷屏）
        while (fromBlock <= safeHead && !ct.IsCancellationRequested)
        {
            // ② 分页拉全（RPC 在事务外；读不全缩半区间重试——对齐 DepositScanManager 的缩半纪律，
            //    单块仍读不全按计数跳过推进：监控卡死比漏一段更危险，跳块轮末统一告警留痕）
            var windowEnd = safeHead;
            List<TokenTransferLog>? logs;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                logs = await PageAllAsync(gateway, token, walletAddress, fromBlock, windowEnd, ct);
                if (logs != null)
                {
                    break;
                }

                if (windowEnd > fromBlock)
                {
                    windowEnd = fromBlock + (windowEnd - fromBlock) / 2; // 缩半重试
                    continue;
                }

                // 单块也读不全（>MaxPages×PageSize 笔）：跳过该块（轮末统一告警），绝不静默卡死游标
                skippedBlocks.Add(fromBlock);
                fromBlock += 1;
                logs = new List<TokenTransferLog>();
                break;
            }

            // ③ 分类 + 落库（事务），游标随每窗持久化推进——预算取消/进程重启不会把已扫窗
            //    的分页成本白做一遍（大积压场景的活锁防线）；重扫由唯一键吸收
            var newAnomalies = await PersistWindowAsync(network, token, logs, walletAddress, ct);
            roundAnomalies.AddRange(newAnomalies);
            fromBlock = windowEnd + 1;
            if (fromBlock > effectiveHead)
            {
                break; // 全窗完成，轮末统一写终值（含跳块留痕/清错）
            }

            await SaveStateAsync(network, token, windowEnd, RenderSkipError(skippedBlocks));
        }

        if (!ct.IsCancellationRequested)
        {
            await SaveStateAsync(network, token, effectiveHead, RenderSkipError(skippedBlocks));
        }

        // ④ 轮级统一出口：未匹配出账聚合一条告警（私钥泄漏检测信号不能因分窗而刷屏/降级）+
        //    跳块聚合一条告警；SignalR 逐笔推送（失败不挡轮次）
        await AlertAndPushAsync(token, roundAnomalies, skippedBlocks);
    }

    /// <summary>跳块留痕文案：一轮多块全量列出（运维据此评估监控缺口范围）。</summary>
    private static string? RenderSkipError(List<long> skippedBlocks)
    {
        if (skippedBlocks.Count == 0)
        {
            return null;
        }

        return skippedBlocks.Count == 1
            ? $"块 {skippedBlocks[0]} 流水超分页上限，已跳过"
            : $"跳过 {skippedBlocks.Count} 块（{skippedBlocks.Min()}..{skippedBlocks.Max()}）流水超分页上限";
    }

    /// <summary>
    /// 区间分页拉全；任何一页报未读完且翻页超限时返回 null（区间太大）。
    /// 返回前去重 (TxHash, LogIndex)——tokentx 分页边界重叠时同一笔可能出现两页
    ///（幂等预查只挡 DB 既有行，批内重复会撞唯一键炸掉整个事务）。
    /// </summary>
    private async Task<List<TokenTransferLog>?> PageAllAsync(
        IBscChainGateway gateway, ColToken token, string walletAddress,
        long fromBlock, long toBlock, CancellationToken ct)
    {
        var logs = new List<TokenTransferLog>();
        var page = 1;
        while (true)
        {
            var result = await gateway.GetTransfersForAddressAsync(
                token.ContractAddress, walletAddress, fromBlock, toBlock, page, PageSize, ct);
            logs.AddRange(result.Logs);
            if (!result.Incomplete)
            {
                return logs
                    .GroupBy(l => (l.TxHash, l.LogIndex))
                    .Select(g => g.First())
                    .ToList();
            }

            page++;
            if (page > MaxPages)
            {
                return null; // 区间读不全：调用方缩半或跳块
            }
        }
    }

    private async Task<List<ColHotWalletTxn>> PersistWindowAsync(
        ColNetwork network, ColToken token, List<TokenTransferLog> logs,
        string walletAddress, CancellationToken ct)
    {
        var newAnomalies = new List<ColHotWalletTxn>();
        if (logs.Count == 0)
        {
            return newAnomalies; // 空窗无需开事务（游标由轮末统一推进）
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var hashes = logs.Select(l => l.TxHash).Distinct().ToList();
        var existing = (await dbContext.HotWalletTxns.AsNoTracking()
                .Where(t => hashes.Contains(t.TxHash))
                .Select(t => new { t.TxHash, t.LogIndex })
                .ToListAsync(ct))
            .Select(x => (x.TxHash, x.LogIndex))
            .ToHashSet();
        // 一哈希多单（理论上广播 CAS 保证唯一；数据异常时取最小 Id——匹配到哪单可追溯）
        var matchedWithdraws = (await dbContext.WithdrawOrders.AsNoTracking()
                .Where(o => o.TxHash != null && hashes.Contains(o.TxHash))
                .Select(o => new { o.TxHash, o.Id })
                .ToListAsync(ct))
            .GroupBy(x => x.TxHash)
            .ToDictionary(g => g.Key!, g => g.OrderBy(x => x.Id).First().Id);

        foreach (var log in logs)
        {
            if (existing.Contains((log.TxHash, log.LogIndex)))
            {
                continue;
            }

            var isOut = string.Equals(log.From, walletAddress, StringComparison.OrdinalIgnoreCase);
            var scaled = DepositScanManager.TryScaleRaw(log.RawAmount, token.Decimals, out var overflow);
            ColHotWalletTxn row;
            if (!isOut)
            {
                row = BuildRow(network, token, log, HotWalletDirection.In, scaled,
                    HotWalletTxnStatus.ExternalIn, matchedWithdrawId: null, now);
            }
            else if (matchedWithdraws.TryGetValue(log.TxHash, out var withdrawId))
            {
                row = BuildRow(network, token, log, HotWalletDirection.Out, scaled,
                    HotWalletTxnStatus.Matched, withdrawId, now);
            }
            else
            {
                row = BuildRow(network, token, log, HotWalletDirection.Out, scaled,
                    HotWalletTxnStatus.Anomaly, matchedWithdrawId: null, now);
                newAnomalies.Add(row);
            }

            dbContext.HotWalletTxns.Add(row);
        }

        await dbContext.SaveChangesAsync(ct);
        await uow.CompleteAsync();
        return newAnomalies;
    }

    /// <summary>
    /// 轮级统一告警/推送出口：未匹配出账一条聚合告警 + SignalR 逐笔推送；跳块一条聚合告警。
    /// （每轮每币各至多一条 [Alert:WithdrawHotWallet]——分窗不应改变告警纪律）
    /// </summary>
    private async Task AlertAndPushAsync(
        ColToken token, List<ColHotWalletTxn> newAnomalies, List<long> skippedBlocks)
    {
        if (skippedBlocks.Count > 0)
        {
            var range = skippedBlocks.Count == 1
                ? $"块 {skippedBlocks[0]}"
                : $"{skippedBlocks.Count} 块（{skippedBlocks.Min()}..{skippedBlocks.Max()}）";
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                $"热钱包监控：{token.Symbol} 已跳过 {range}（单块流水超出分页上限）",
                "被跳块内的热钱包转账本轮未入库（Etherscan 分页读不全）。请人工核查这些块的出站流水后决定是否补救。");
        }

        if (newAnomalies.Count == 0)
        {
            return;
        }

        var first = newAnomalies[0];
        var firstAmount = first.Amount == null
            ? first.RawAmount
            : CollectionAmounts.Format(first.Amount.Value);
        await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
            $"热钱包监控：{token.Symbol} 新增未匹配出账 {newAnomalies.Count} 笔",
            $"首笔 TxHash {first.TxHash}，金额 {firstAmount} → {first.To}。" +
            "出站流水匹配不到任何提现单——请立即在管理端核实（私钥泄漏/误操作都可能）");
        if (_pushNotifier == null)
        {
            return;
        }

        foreach (var anomaly in newAnomalies)
        {
            try
            {
                await _pushNotifier.PushHotWalletChangedAsync(new ChainHotWalletChangedEto
                {
                    TxnId = anomaly.Id,
                    Symbol = anomaly.Symbol,
                    Status = anomaly.Status.ToString(),
                    Direction = anomaly.Direction.ToString(),
                    TxHash = anomaly.TxHash,
                    Amount = anomaly.Amount == null ? anomaly.RawAmount : CollectionAmounts.Format(anomaly.Amount.Value),
                    To = anomaly.To,
                    BlockNumber = anomaly.BlockNumber,
                });
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "热钱包异常 SignalR 推送失败（不影响监控）");
            }
        }
    }

    private ColHotWalletTxn BuildRow(
        ColNetwork network, ColToken token, TokenTransferLog log, HotWalletDirection direction,
        decimal? scaled, HotWalletTxnStatus status, Guid? matchedWithdrawId, DateTime now)
    {
        return new ColHotWalletTxn(_guidGenerator.Create())
        {
            NetworkId = network.Id,
            TokenId = token.Id,
            Symbol = token.Symbol,
            TxHash = log.TxHash,
            LogIndex = log.LogIndex,
            From = log.From,
            To = log.To,
            Direction = direction,
            RawAmount = log.RawAmount,
            Amount = scaled,
            BlockNumber = log.BlockNumber,
            Status = status,
            MatchedWithdrawId = matchedWithdrawId,
            CreationTime = now,
        };
    }

    private async Task SaveStateAsync(
        ColNetwork network, ColToken token, long? lastScannedBlock, string? lastError)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;
        var state = await dbContext.HotWalletWatchStates
            .FirstOrDefaultAsync(s => s.TokenId == token.Id);
        if (state == null)
        {
            dbContext.HotWalletWatchStates.Add(new ColHotWalletWatchState(_guidGenerator.Create())
            {
                NetworkId = network.Id,
                TokenId = token.Id,
                Symbol = token.Symbol,
                LastScannedBlock = lastScannedBlock,
                LastScanAt = now,
                LastError = lastError,
                CreationTime = now,
                UpdatedAt = now,
            });
        }
        else
        {
            state.LastScannedBlock = lastScannedBlock;
            state.LastScanAt = now;
            state.LastError = lastError;
            state.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }
}
