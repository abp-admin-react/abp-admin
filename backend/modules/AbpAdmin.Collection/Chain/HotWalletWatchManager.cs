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
/// 入站记为外部补币（提示性）。幂等：(TxHash, LogIndex) 唯一吸收重扫；游标只在整个区间
/// 分页读全后推进，读不全记 LastError 下一轮重扫。首跑基线 = 当时安全链头（无历史基线可
/// 完整回扫；余额核对点重放留二期）。私钥不参与——只读监控，不碰签名面。
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
            await SaveStateAsync(network, token, safeHead, lastError: null, baseline: true);
            Logger.LogInformation("热钱包监控 {Symbol} 首跑基线 = 安全链头 {Head}（历史区间不回扫）",
                token.Symbol, safeHead);
            return;
        }

        var fromBlock = state.LastScannedBlock.HasValue ? state.LastScannedBlock.Value + 1 : safeHead;
        if (fromBlock > safeHead)
        {
            return; // 无新区块
        }

        // ② 分页拉全（RPC 在事务外；分页模板抄哈希认领）
        var logs = new List<TokenTransferLog>();
        var page = 1;
        while (true)
        {
            var result = await gateway.GetTransfersForAddressAsync(
                token.ContractAddress, walletAddress, fromBlock, safeHead, page, PageSize, ct);
            logs.AddRange(result.Logs);
            if (!result.Incomplete)
            {
                break;
            }

            page++;
            if (page > MaxPages)
            {
                await SaveStateAsync(network, token, state.LastScannedBlock,
                    $"区间 [{fromBlock},{safeHead}] 分页超过 {MaxPages} 页未读全，游标未推进", baseline: false);
                Logger.LogError("热钱包监控 {Symbol} 区间 [{From},{To}] 分页超限，本轮放弃（游标未动）",
                    token.Symbol, fromBlock, safeHead);
                return;
            }
        }

        // ③ 分类 + 落库 + 推游标（事务：流水与游标同提交——游标先走会丢行，行先走重扫由唯一键吸收）
        var newAnomalies = new List<ColHotWalletTxn>();
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var now = _clock.Now;

            var hashes = logs.Select(l => l.TxHash).Distinct().ToList();
            var existing = (await dbContext.HotWalletTxns.AsNoTracking()
                    .Where(t => hashes.Contains(t.TxHash))
                    .Select(t => new { t.TxHash, t.LogIndex })
                    .ToListAsync(ct))
                .Select(x => (x.TxHash, x.LogIndex))
                .ToHashSet();
            var matchedWithdraws = (await dbContext.WithdrawOrders.AsNoTracking()
                    .Where(o => o.TxHash != null && hashes.Contains(o.TxHash))
                    .Select(o => new { o.TxHash, o.Id })
                    .ToListAsync(ct))
                .GroupBy(x => x.TxHash)
                .ToDictionary(g => g.Key!, g => g.First().Id);

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

            await WriteStateAsync(dbContext, network, token, safeHead, lastError: null, now,
                () => _guidGenerator.Create());
            await dbContext.SaveChangesAsync(ct);
            await uow.CompleteAsync();
        }

        // ④ 告警（每币每轮聚合一条防刷屏）+ SignalR 推送（失败不挡轮次）
        if (newAnomalies.Count > 0)
        {
            var first = newAnomalies[0];
            var firstAmount = first.Amount == null
                ? first.RawAmount
                : CollectionAmounts.Format(first.Amount.Value);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                $"热钱包监控：{token.Symbol} 新增未匹配出账 {newAnomalies.Count} 笔",
                $"首笔 TxHash {first.TxHash}，金额 {firstAmount} → {first.To}。" +
                "出站流水匹配不到任何提现单——请立即在管理端核实（私钥泄漏/误操作都可能）");
            if (_pushNotifier != null)
            {
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
        ColNetwork network, ColToken token, long? lastScannedBlock, string? lastError, bool baseline)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await WriteStateAsync(dbContext, network, token, lastScannedBlock, lastError, _clock.Now,
            () => _guidGenerator.Create());
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    private async Task WriteStateAsync(
        CollectionDbContext dbContext, ColNetwork network, ColToken token,
        long? lastScannedBlock, string? lastError, DateTime now, Func<Guid> newId)
    {
        var state = await dbContext.HotWalletWatchStates
            .FirstOrDefaultAsync(s => s.TokenId == token.Id);
        if (state == null)
        {
            dbContext.HotWalletWatchStates.Add(new ColHotWalletWatchState(newId())
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
            return;
        }

        state.LastScannedBlock = lastScannedBlock;
        state.LastScanAt = now;
        state.LastError = lastError;
        state.UpdatedAt = now;
    }
}
