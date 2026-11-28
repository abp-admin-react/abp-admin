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
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 热钱包逐笔监控（v2，只读不碰私钥）：
/// 1) 入口经 VerifyDeployedCodeAsync 多节点/双次复核防 EIP-7702 委托：确认非空即自动触发 Withdraw.Halt，读数不一致仅告警；
/// 2) RPC eth_getLogs 优先、Etherscan 回退，区间读不全缩半分窗，单块仍超上限时跳块留痕 + 告警；
/// 3) 出站结合 ColWithdrawOrders 与 ColChainTxAttempts 对账（迟到哈希自动晋升 Matched，Self 自转按净额 0 处理）；
/// 4) (safeHead, head] 未确认出站仅发实时预警（不自动拉响 Halt）；
/// 5) 每 5 分钟在安全块按绑定热钱包地址执行余额 Checkpoint 对账，发现已确认异常出账或余额缺口告警（可选 AutoHaltOnAnomaly 自动熔断）。
/// </summary>
public class HotWalletWatchManager : DomainService
{
    private const int PageSize = 100;
    private const int MaxPages = 20;
    internal static readonly TimeSpan BalanceCheckpointInterval = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, DateTime> UnconfirmedAlertedAt = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ChainAlertService _alerts;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IChainPushNotifier? _pushNotifier;
    private readonly ISettingProvider? _settings;
    private readonly ISettingManager? _settingManager;

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
        _settings = serviceProvider?.GetService<ISettingProvider>();
        _settingManager = serviceProvider?.GetService<ISettingManager>();
    }

    /// <summary>一轮：热钱包未配置整轮跳过；先做 EIP-7702 防委托检测，再逐启用币扫描。</summary>
    public virtual async Task RunRoundAsync(
        ColNetwork network, IBscChainGateway gateway, int confirmations, CancellationToken ct)
    {
        var walletAddress = network.WithdrawVaultAddress;
        if (string.IsNullOrEmpty(walletAddress))
        {
            return;
        }

        // 防 EIP-7702 恶意委托：热钱包 EOA 经多节点/双次复核出现非空合法字节码立即触发 Halt 总闸并告警
        try
        {
            var (codeConfirmed, code, codeDisagreement) = await gateway.VerifyDeployedCodeAsync(walletAddress, ct);
            var preview = code.Length > 24 ? code[..24] + "..." : code;
            if (codeConfirmed)
            {
                Logger.LogCritical(
                    "{Prefix}热钱包 {Wallet} 检测到非空字节码/EIP-7702 委托（code={Code}），立即开启提现停机总闸",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, walletAddress, preview);
                await TripWithdrawHaltAsync();
                await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                    "热钱包检测到非空合约代码/EIP-7702 委托",
                    $"热钱包 {walletAddress} 链上 eth_getCode 非空（{preview}），存在私钥泄露或 EIP-7702 委托风险，系统已自动拉响提现停机总闸（Halt=true），请立即人工核查与转移资金。");
            }
            else if (codeDisagreement)
            {
                Logger.LogCritical(
                    "{Prefix}热钱包 {Wallet} eth_getCode 多节点读数不一致（首次={Code}），暂未自动拉响总闸，请立即核查节点一致性",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, walletAddress, preview);
                await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                    "热钱包 eth_getCode 多节点读数不一致",
                    $"热钱包 {walletAddress} 在不同节点/复核中出现不一致的 eth_getCode 响应（首读={preview}），已触发告警保留服务，请核实链上代码状态。");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "热钱包 eth_getCode 检测异常，继续执行本轮流水监控: Wallet={Wallet}", walletAddress);
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
                await RunTokenRoundAsync(network, token, walletAddress!, gateway, head, safeHead, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "热钱包监控 {Symbol} 一轮失败，跳过该币本轮", token.Symbol);
            }
        }
    }

    private async Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, string walletAddress,
        IBscChainGateway gateway, long head, long safeHead, CancellationToken ct)
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

        // 先对既往未解释异常单做迟到哈希自动晋升（Anomaly -> Matched）
        await ReconcileOpenAnomaliesAsync(token.Id, ct);

        var fromBlock = state.LastScannedBlock.HasValue ? state.LastScannedBlock.Value + 1 : safeHead;
        var effectiveHead = Math.Max(safeHead, state.LastScannedBlock ?? safeHead);
        var skippedBlocks = new List<long>();
        var roundAnomalies = new List<ColHotWalletTxn>();
        while (fromBlock <= safeHead && !ct.IsCancellationRequested)
        {
            var windowEnd = safeHead;
            List<TokenTransferLog>? logs;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                logs = await ReadWindowLogsAsync(gateway, token, walletAddress, fromBlock, windowEnd, ct);
                if (logs != null)
                {
                    break;
                }

                if (windowEnd > fromBlock)
                {
                    windowEnd = fromBlock + (windowEnd - fromBlock) / 2;
                    continue;
                }

                skippedBlocks.Add(fromBlock);
                fromBlock += 1;
                logs = new List<TokenTransferLog>();
                break;
            }

            var newAnomalies = await PersistWindowAsync(network, token, logs, walletAddress, ct);
            roundAnomalies.AddRange(newAnomalies);
            fromBlock = windowEnd + 1;
            if (fromBlock > effectiveHead)
            {
                break;
            }

            await SaveStateAsync(network, token, windowEnd, RenderSkipError(skippedBlocks));
        }

        if (!ct.IsCancellationRequested)
        {
            await SaveStateAsync(network, token, effectiveHead, RenderSkipError(skippedBlocks));
        }

        await AlertAndPushAsync(token, roundAnomalies, skippedBlocks);

        if (!ct.IsCancellationRequested && head > effectiveHead)
        {
            await PeekUnconfirmedOutboundAsync(token, walletAddress, gateway, effectiveHead + 1, head, ct);
        }

        if (!ct.IsCancellationRequested)
        {
            await RunBalanceCheckpointAsync(network, token, walletAddress, gateway, effectiveHead, ct);
        }
    }

    /// <summary>
    /// 优先用 RPC 节点过滤 eth_getLogs 拉取转出/转入，节点不支持或截断时回退 Etherscan 分页。
    /// </summary>
    private async Task<List<TokenTransferLog>?> ReadWindowLogsAsync(
        IBscChainGateway gateway, ColToken token, string walletAddress,
        long fromBlock, long toBlock, CancellationToken ct)
    {
        var rpcTruncated = false;
        Exception? rpcError = null;
        try
        {
            var (outbound, outComplete) = await gateway.GetFilteredTransferLogsAsync(
                token.ContractAddress, fromAddress: walletAddress, toAddress: null, fromBlock, toBlock, ct);
            var (inbound, inComplete) = await gateway.GetFilteredTransferLogsAsync(
                token.ContractAddress, fromAddress: null, toAddress: walletAddress, fromBlock, toBlock, ct);
            if (outComplete && inComplete)
            {
                return outbound.Concat(inbound)
                    .GroupBy(l => (l.TxHash.ToLowerInvariant(), l.LogIndex))
                    .Select(g => g.First())
                    .ToList();
            }

            rpcTruncated = true;
        }
        catch (Exception ex) when (ex is ChainRpcException or System.Net.Http.HttpRequestException)
        {
            rpcError = ex;
        }

        try
        {
            return await PageAllAsync(gateway, token, walletAddress, fromBlock, toBlock, ct);
        }
        catch (Exception ex) when (ex is ChainRpcException or System.Net.Http.HttpRequestException)
        {
            if (rpcTruncated)
            {
                // RPC 节点本身可用、只是区间过宽且无可用 Etherscan 回退：交由外层缩半区间重试
                return null;
            }

            Logger.LogWarning(rpcError ?? ex,
                "热钱包监控 {Symbol} 区间 {From}..{To} RPC 与 Etherscan 通道均失败",
                token.Symbol, fromBlock, toBlock);
            throw;
        }
    }

    private async Task ReconcileOpenAnomaliesAsync(Guid tokenId, CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var openAnomalies = await dbContext.HotWalletTxns
            .Where(t => t.TokenId == tokenId && t.Status == HotWalletTxnStatus.Anomaly)
            .Select(t => new { t.Id, t.TxHash })
            .ToListAsync(ct);
        if (openAnomalies.Count == 0)
        {
            await uow.CompleteAsync(ct);
            return;
        }

        var hashes = openAnomalies.Select(a => a.TxHash.ToLowerInvariant()).Distinct().ToList();
        var matchMap = await LoadWithdrawMatchMapAsync(dbContext, hashes, ct);
        var promotedCount = 0;
        foreach (var anomaly in openAnomalies)
        {
            if (!matchMap.TryGetValue(anomaly.TxHash.ToLowerInvariant(), out var withdrawId))
            {
                continue;
            }

            var updated = await dbContext.HotWalletTxns
                .Where(t => t.Id == anomaly.Id && t.Status == HotWalletTxnStatus.Anomaly)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, HotWalletTxnStatus.Matched)
                    .SetProperty(t => t.MatchedWithdrawId, withdrawId), ct);
            promotedCount += updated;
        }

        await uow.CompleteAsync(ct);
        if (promotedCount > 0)
        {
            Logger.LogInformation("热钱包监控：将 {Count} 笔迟到落库哈希的异常流水自动晋升为 Matched", promotedCount);
        }
    }

    private async Task PeekUnconfirmedOutboundAsync(
        ColToken token, string walletAddress, IBscChainGateway gateway,
        long fromBlock, long toBlock, CancellationToken ct)
    {
        if (fromBlock > toBlock)
        {
            return;
        }

        try
        {
            var (outbound, _) = await gateway.GetFilteredTransferLogsAsync(
                token.ContractAddress, fromAddress: walletAddress, toAddress: null, fromBlock, toBlock, ct);
            if (outbound.Count == 0)
            {
                return;
            }

            var hashes = outbound.Select(l => l.TxHash.ToLowerInvariant()).Distinct().ToList();
            Dictionary<string, Guid> knownWithdraws;
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
            {
                var dbContext = await _dbContextProvider.GetDbContextAsync();
                knownWithdraws = await LoadWithdrawMatchMapAsync(dbContext, hashes, ct);
                await uow.CompleteAsync(ct);
            }

            var now = _clock.Now;
            var unknown = new List<TokenTransferLog>();
            foreach (var log in outbound)
            {
                if (string.Equals(log.To, walletAddress, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = log.TxHash.ToLowerInvariant();
                if (knownWithdraws.ContainsKey(hash))
                {
                    continue;
                }

                if (UnconfirmedAlertedAt.TryGetValue(hash, out var last) && now - last < TimeSpan.FromMinutes(30))
                {
                    continue;
                }

                UnconfirmedAlertedAt[hash] = now;
                unknown.Add(log);
            }

            if (unknown.Count == 0)
            {
                return;
            }

            var first = unknown[0];
            var amount = DepositScanManager.TryScaleRaw(first.RawAmount, token.Decimals, out _);
            var amountLabel = amount.HasValue ? CollectionAmounts.Format(amount.Value) : first.RawAmount;
            Logger.LogCritical(
                "{Prefix}热钱包未确认区发现未匹配出账（仅实时告警，等待确认后再决定熔断）: Symbol={Symbol}, Tx={Tx}",
                CollectionConsts.AlertPrefixWithdrawHotWallet, token.Symbol, first.TxHash);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                $"热钱包未确认异常出账预警：{token.Symbol} 发现 {unknown.Count} 笔",
                $"未确认区块（{fromBlock}..{toBlock}）检测到热钱包 {walletAddress} 未匹配出账：Tx={first.TxHash}，金额={amountLabel} → {first.To}。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug(ex, "热钱包未确认出账窥视异常（不影响已确认主扫描）");
        }
    }

    private async Task RunBalanceCheckpointAsync(
        ColNetwork network, ColToken token, string walletAddress,
        IBscChainGateway gateway, long safeHead, CancellationToken ct)
    {
        try
        {
            var now = _clock.Now;
            ColHotWalletWatchState? state;
            using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
            {
                var readDb = await _dbContextProvider.GetDbContextAsync();
                state = await readDb.HotWalletWatchStates.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TokenId == token.Id, ct);
                await readUow.CompleteAsync(ct);
            }

            if (state == null)
            {
                return;
            }

            if (state.LastCheckpointAt.HasValue && now - state.LastCheckpointAt.Value < BalanceCheckpointInterval)
            {
                return;
            }

            var rawActual = await gateway.GetTokenBalanceRawAtBlockAsync(token.ContractAddress, walletAddress, safeHead, ct);
            if (!BigInteger.TryParse(rawActual, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actualWei))
            {
                return;
            }

            var normalizedWallet = walletAddress.ToLowerInvariant();
            if (!string.Equals(state.LastCheckpointWalletAddress, normalizedWallet, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInformation(
                    "热钱包地址变更或首次绑定（{OldWallet} -> {NewWallet}），重置 {Symbol} 余额核对点基线（SafeHead={SafeHead}）",
                    state.LastCheckpointWalletAddress ?? "空", normalizedWallet, token.Symbol, safeHead);
            }
            else if (!string.IsNullOrEmpty(state.LastCheckpointBalanceRaw)
                && BigInteger.TryParse(state.LastCheckpointBalanceRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prevWei)
                && state.LastCheckpointBlock is long prevBlock
                && safeHead > prevBlock)
            {
                List<(HotWalletDirection Direction, string From, string To, string RawAmount)> deltas;
                using (var deltaUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
                {
                    var deltaDb = await _dbContextProvider.GetDbContextAsync();
                    var rows = await deltaDb.HotWalletTxns.AsNoTracking()
                        .Where(t => t.TokenId == token.Id && t.BlockNumber > prevBlock && t.BlockNumber <= safeHead)
                        .Select(t => new { t.Direction, t.From, t.To, t.RawAmount })
                        .ToListAsync(ct);
                    deltas = rows.Select(r => (r.Direction, r.From, r.To, r.RawAmount)).ToList();
                    await deltaUow.CompleteAsync(ct);
                }

                var expectedWei = prevWei;
                foreach (var (direction, from, to, raw) in deltas)
                {
                    var fromIsWallet = string.Equals(from, normalizedWallet, StringComparison.OrdinalIgnoreCase);
                    var toIsWallet = string.Equals(to, normalizedWallet, StringComparison.OrdinalIgnoreCase);
                    if (!fromIsWallet && !toIsWallet)
                    {
                        continue;
                    }

                    if (direction == HotWalletDirection.Self || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wei))
                    {
                        continue;
                    }

                    expectedWei += direction == HotWalletDirection.In ? wei : -wei;
                }

                if (actualWei < expectedWei)
                {
                    var shortfall = expectedWei - actualWei;
                    var scaledShortfall = DepositScanManager.TryScaleRaw(shortfall.ToString(CultureInfo.InvariantCulture), token.Decimals, out _);
                    var shortfallText = scaledShortfall.HasValue ? CollectionAmounts.Format(scaledShortfall.Value) : shortfall.ToString(CultureInfo.InvariantCulture);
                    await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                        $"热钱包余额核对点异常：{token.Symbol} 出现未解释减少",
                        $"块 {prevBlock}..{safeHead} 期末余额低于期初+流水预期（缺口 {shortfallText} {token.Symbol}，实际 raw={actualWei}，预期 raw={expectedWei}），请立即核查。");
                    if (await ShouldAutoHaltAsync())
                    {
                        Logger.LogCritical(
                            "{Prefix}热钱包余额核对点缺口触发自动熔断 Halt=true: Symbol={Symbol}, Shortfall={Shortfall}",
                            CollectionConsts.AlertPrefixWithdrawHotWallet, token.Symbol, shortfallText);
                        await TripWithdrawHaltAsync();
                    }
                }
            }

            using var saveUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var saveDb = await _dbContextProvider.GetDbContextAsync();
            await saveDb.HotWalletWatchStates
                .Where(s => s.Id == state.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastCheckpointWalletAddress, normalizedWallet)
                    .SetProperty(x => x.LastCheckpointBalanceRaw, actualWei.ToString(CultureInfo.InvariantCulture))
                    .SetProperty(x => x.LastCheckpointBlock, safeHead)
                    .SetProperty(x => x.LastCheckpointAt, now)
                    .SetProperty(x => x.UpdatedAt, now), ct);
            await saveUow.CompleteAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "热钱包余额 Checkpoint 核对失败（{Symbol}, SafeHead={SafeHead}）", token.Symbol, safeHead);
        }
    }

    private async Task<bool> ShouldAutoHaltAsync()
    {
        return _settings != null && await _settings.IsTrueAsync(CollectionSettings.Withdraw.AutoHaltOnAnomaly);
    }

    private async Task TripWithdrawHaltAsync()
    {
        if (_settingManager == null)
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _settingManager.SetGlobalAsync(CollectionSettings.Withdraw.Halt, "true");
        await uow.CompleteAsync();
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
            if (result.HasUnresolvedLogs)
            {
                return null;
            }

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
                return null;
            }
        }
    }

    private static async Task<Dictionary<string, Guid>> LoadWithdrawMatchMapAsync(
        CollectionDbContext dbContext, List<string> hashes, CancellationToken ct)
    {
        var map = (await dbContext.WithdrawOrders.AsNoTracking()
                .Where(o => o.TxHash != null && hashes.Contains(o.TxHash))
                .Select(o => new { TxHash = o.TxHash!, o.Id })
                .ToListAsync(ct))
            .GroupBy(x => x.TxHash.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First().Id, StringComparer.OrdinalIgnoreCase);

        var attempts = await dbContext.ChainTxAttempts.AsNoTracking()
            .Where(a => a.Purpose == ChainTxPurpose.WithdrawPayout && a.RefId != null && hashes.Contains(a.TxHash))
            .Select(a => new { a.TxHash, RefId = a.RefId!.Value })
            .ToListAsync(ct);
        foreach (var attempt in attempts)
        {
            map.TryAdd(attempt.TxHash.ToLowerInvariant(), attempt.RefId);
        }

        return map;
    }

    private async Task<List<ColHotWalletTxn>> PersistWindowAsync(
        ColNetwork network, ColToken token, List<TokenTransferLog> logs,
        string walletAddress, CancellationToken ct)
    {
        var newAnomalies = new List<ColHotWalletTxn>();
        if (logs.Count == 0)
        {
            return newAnomalies;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var hashes = logs.Select(l => l.TxHash.ToLowerInvariant()).Distinct().ToList();
        var existing = (await dbContext.HotWalletTxns.AsNoTracking()
                .Where(t => hashes.Contains(t.TxHash))
                .Select(t => new { t.TxHash, t.LogIndex })
                .ToListAsync(ct))
            .Select(x => (x.TxHash.ToLowerInvariant(), x.LogIndex))
            .ToHashSet();
        var matchedWithdraws = await LoadWithdrawMatchMapAsync(dbContext, hashes, ct);

        foreach (var log in logs)
        {
            var normalizedHash = log.TxHash.ToLowerInvariant();
            if (existing.Contains((normalizedHash, log.LogIndex)))
            {
                continue;
            }

            var isFrom = string.Equals(log.From, walletAddress, StringComparison.OrdinalIgnoreCase);
            var isTo = string.Equals(log.To, walletAddress, StringComparison.OrdinalIgnoreCase);
            var scaled = DepositScanManager.TryScaleRaw(log.RawAmount, token.Decimals, out _);
            ColHotWalletTxn row;
            if (isFrom && isTo)
            {
                row = BuildRow(network, token, log, HotWalletDirection.Self, scaled,
                    HotWalletTxnStatus.ExternalIn, matchedWithdrawId: null, now);
            }
            else if (!isFrom)
            {
                row = BuildRow(network, token, log, HotWalletDirection.In, scaled,
                    HotWalletTxnStatus.ExternalIn, matchedWithdrawId: null, now);
            }
            else if (matchedWithdraws.TryGetValue(normalizedHash, out var withdrawId))
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
        await uow.CompleteAsync(ct);
        return newAnomalies;
    }

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
                "被跳块内的热钱包转账本轮未入库（RPC 与 Etherscan 通道均未完整读出该块）。请人工核查这些块的出站流水后决定是否补救。");
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
        if (await ShouldAutoHaltAsync())
        {
            Logger.LogCritical(
                "{Prefix}热钱包已确认未匹配出账触发自动熔断 Halt=true: Symbol={Symbol}, Count={Count}, FirstTx={Tx}",
                CollectionConsts.AlertPrefixWithdrawHotWallet, token.Symbol, newAnomalies.Count, first.TxHash);
            await TripWithdrawHaltAsync();
        }

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
            TxHash = log.TxHash.ToLowerInvariant(),
            LogIndex = log.LogIndex,
            From = log.From.ToLowerInvariant(),
            To = log.To.ToLowerInvariant(),
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
