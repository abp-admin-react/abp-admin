using AbpAdmin.Collection.Ledger;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Withdrawals;

/// <summary>
/// 提现广播与卡单恢复管理器：每轮依次执行回执确认/卡单仲裁、小额自动审与待广播出款。
/// 发送前在 AccountSlot 锁内通过 AllocateSerialNonceAsync 单调分配 nonce 并预落库 ColChainTxAttempts，
/// 按 RetryCount 抬价（热钱包 BNB 不足拒绝静默压价）；Broadcasting 阶段全候选查回执并支持同 nonce
/// 3 分钟卡死队头加价替换（allowSiblingAttemptHash）；无哈希超时先查本地签名尝试，再按原发送方与 nonce 仲裁。
/// </summary>
public class WithdrawBroadcastManager : DomainService
{
    /// <summary>签名临界区（进程内串行；跨实例由 worker 的 Redis 轮锁兜底）。</summary>
    private static readonly SemaphoreSlim SignGate = new(1, 1);

    /// <summary>提现队头未出块时同 nonce 抬价替换等待期。</summary>
    internal static readonly TimeSpan WithdrawHeadReplaceAfter = TimeSpan.FromMinutes(3);

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settings;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly WithdrawManager _withdraws;
    private readonly CollectionLedgerPoster _ledger;
    private readonly WithdrawGuard _guard;
    private readonly IClock _clock;
    private readonly Volo.Abp.Security.Encryption.IStringEncryptionService _encryptor;
    private readonly ChainKeyProvider _chainKeyProvider;
    private readonly ChainAlertService _alerts;

    public WithdrawBroadcastManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings,
        IUnitOfWorkManager unitOfWorkManager,
        WithdrawManager withdraws,
        CollectionLedgerPoster ledger,
        WithdrawGuard guard,
        IClock clock,
        Volo.Abp.Security.Encryption.IStringEncryptionService encryptor,
        ChainKeyProvider chainKeyProvider,
        ChainAlertService alerts)
    {
        _dbContextProvider = dbContextProvider;
        _settings = settings;
        _unitOfWorkManager = unitOfWorkManager;
        _withdraws = withdraws;
        _ledger = ledger;
        _guard = guard;
        _clock = clock;
        _encryptor = encryptor;
        _chainKeyProvider = chainKeyProvider;
        _alerts = alerts;
    }

    /// <summary>一轮：卡单恢复 → 小额自动审 → 广播队列。任一段失败不挡下一段。</summary>
    public virtual async Task RunRoundAsync(
        ColNetwork network, IBscChainGateway gateway,
        int autoApproveBatch, int broadcastBatch, CancellationToken ct = default)
    {
        await WithDbScopeAsync(async () =>
        {
            try
            {
                await RecoverStuckBroadcastingAsync(network, gateway, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "回执确认/仲裁段异常，本轮跳过该段继续自动审与广播");
            }

            try
            {
                await AutoApproveSmallOrdersAsync(network, gateway, autoApproveBatch, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "小额自动审段异常，本轮跳过该段继续广播");
            }

            try
            {
                await BroadcastQueueAsync(network, gateway, broadcastBatch, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "广播队列段异常，等下一轮");
            }
        });
    }

    /// <summary>无环境工作单元时自开一个非事务 UoW（读 + 守卫式 ExecuteUpdate 够用）。</summary>
    private async Task WithDbScopeAsync(Func<Task> body)
    {
        if (_unitOfWorkManager.Current != null)
        {
            await body();
            return;
        }

        using var uow = _unitOfWorkManager.Begin();
        await body();
        await uow.CompleteAsync();
    }

    // ── ① 回执确认 + 卡单恢复 ─────────────────────────────────────────────────────────────

    public virtual Task RecoverStuckBroadcastingAsync(
        ColNetwork network, IBscChainGateway gateway, CancellationToken ct = default)
    {
        return WithDbScopeAsync(() => RecoverStuckCoreAsync(network, gateway, ct));
    }

    private async Task RecoverStuckCoreAsync(
        ColNetwork network, IBscChainGateway gateway, CancellationToken ct)
    {
        var timeoutMinutes = await _settings.GetIntAsync(CollectionSettings.Withdraw.BroadcastTimeoutMinutes, WithdrawDefaults.BroadcastTimeoutMinutes);
        var maxRetries = await _settings.GetIntAsync(CollectionSettings.Withdraw.MaxRetries, WithdrawDefaults.MaxRetries);
        var cutoff = _clock.Now.AddMinutes(-timeoutMinutes);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var tokenCache = new Dictionary<CollectionCurrency, ColToken>();

        // ① 有哈希的 Broadcasting：每轮全候选查回执；长期未出块则对热钱包队头同 nonce 抬价替换
        var withHash = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Broadcasting && o.TxHash != null && o.NetworkName == network.Name)
            .OrderBy(o => o.StatusChangedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var order in withHash)
        {
            try
            {
                var attempts = order.NonceUsed is long usedNonce && WithdrawManager.HasBroadcastSender(order)
                    ? await ChainTxSlotCoordinator.LoadSlotAttemptsAsync(
                        dbContext, network.Id, ChainTxPurpose.WithdrawPayout, order.Id, usedNonce, ct)
                    : new List<ColChainTxAttempt>();

                var candidates = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (seen.Add(order.TxHash!))
                {
                    candidates.Add(order.TxHash!);
                }

                foreach (var a in attempts)
                {
                    if (!string.IsNullOrEmpty(a.TxHash) && seen.Add(a.TxHash))
                    {
                        candidates.Add(a.TxHash);
                    }
                }

                string? winningHash = null;
                TransactionReceipt? winningReceipt = null;
                foreach (var candidateHash in candidates)
                {
                    var candReceipt = await gateway.GetTransactionReceiptAsync(candidateHash, ct);
                    if (candReceipt.Status is 0 or 1)
                    {
                        winningHash = candidateHash.ToLowerInvariant();
                        winningReceipt = candReceipt;
                        break;
                    }
                }

                if (winningReceipt == null || winningHash == null)
                {
                    if (_clock.Now - order.StatusChangedAt > TimeSpan.FromMinutes(timeoutMinutes * 3))
                    {
                        await MarkFailedAsync(order, WithdrawStatus.Broadcasting, "已记录交易哈希但长期未获取回执，需核实链上结果");
                        continue;
                    }

                    await TryReplaceStuckWithdrawHeadAsync(network, gateway, order, attempts, tokenCache, ct);
                    continue;
                }

                var winningAttempt = attempts.FirstOrDefault(a =>
                    string.Equals(a.TxHash, winningHash, StringComparison.OrdinalIgnoreCase));
                var fallbackGasWei = winningAttempt?.GasPriceWei;

                if (winningReceipt.Status == 1)
                {
                    using (var settleUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                    {
                        var settleDb = await _dbContextProvider.GetDbContextAsync();
                        await ChainTxSlotCoordinator.MarkSlotSettledAsync(
                            settleDb, network.Id, ChainTxPurpose.WithdrawPayout, order.Id,
                            order.NonceUsed, winningHash, ChainTxAttemptStatus.Confirmed, ct);
                        await settleUow.CompleteAsync(ct);
                    }

                    await ChainGasFeeRecorder.TryRecordAsync(
                        _unitOfWorkManager, _dbContextProvider, _clock,
                        network.Id, ChainTxPurpose.WithdrawPayout, order.Id,
                        winningHash, winningReceipt, fallbackGasPriceWei: fallbackGasWei,
                        fallbackSenderAddress: order.BroadcastSenderAddress, ct);
                    var isSiblingHash = !string.Equals(winningHash, order.TxHash, StringComparison.OrdinalIgnoreCase);
                    await CompleteFromReceiptAsync(order.Id, winningHash, order, allowSiblingAttemptHash: isSiblingHash);
                }
                else
                {
                    using (var settleUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                    {
                        var settleDb = await _dbContextProvider.GetDbContextAsync();
                        await ChainTxSlotCoordinator.MarkSlotSettledAsync(
                            settleDb, network.Id, ChainTxPurpose.WithdrawPayout, order.Id,
                            order.NonceUsed, winningHash, ChainTxAttemptStatus.Reverted, ct);
                        await settleUow.CompleteAsync(ct);
                    }

                    await ChainGasFeeRecorder.TryRecordAsync(
                        _unitOfWorkManager, _dbContextProvider, _clock,
                        network.Id, ChainTxPurpose.WithdrawPayout, order.Id,
                        winningHash, winningReceipt, fallbackGasPriceWei: fallbackGasWei,
                        fallbackSenderAddress: order.BroadcastSenderAddress, ct);

                    if (order.RetryCount + 1 >= maxRetries)
                    {
                        // 回执失败（revert）= 链上可证未出款：自动退款终态，不等人
                        await AutoRefundAsync(order, WithdrawStatus.Broadcasting,
                            $"链上回执失败且重试次数用尽（RetryCount={order.RetryCount}），自动退款");
                    }
                    else
                    {
                        await RequeueToProcessingAsync(order, WithdrawStatus.Broadcasting, "链上回执失败，重试", ct);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "回执确认单笔异常，跳过本单: OrderId={OrderId}", order.Id);
            }
        }

        // ② 无哈希且超时的 Broadcasting：正向证据才能自动重试或退款。
        var stuckNoHash = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Broadcasting && o.TxHash == null && o.StatusChangedAt < cutoff
                        && o.NetworkName == network.Name)
            .OrderBy(o => o.StatusChangedAt)
            .Take(20)
            .ToListAsync(ct);

        var manualReviewAfter = TimeSpan.FromMinutes(timeoutMinutes * 3L);
        foreach (var order in stuckNoHash)
        {
            try
            {
                if (!WithdrawManager.HasBroadcastSender(order))
                {
                    await MarkFailedAsync(order, WithdrawStatus.Broadcasting, "无哈希且缺少原发送地址或 nonce，需人工核实链上交易");
                    continue;
                }

                var sender = order.BroadcastSenderAddress!;

                if (await _withdraws.HasOtherCompletedAttemptAsync(order, ct))
                {
                    if (order.RetryCount + 1 >= maxRetries)
                    {
                        await AutoRefundAsync(order, WithdrawStatus.Broadcasting,
                            $"原发送方的 nonce {order.NonceUsed} 已被另一笔提现占用，重试次数用尽，自动退款");
                    }
                    else
                    {
                        await RequeueToProcessingAsync(order, WithdrawStatus.Broadcasting,
                            $"原发送方的 nonce {order.NonceUsed} 已被另一笔提现占用，自动重播（第 {order.RetryCount + 1} 次）", ct);
                    }

                    continue;
                }

                if (!tokenCache.TryGetValue(order.Currency, out var token))
                {
                    tokenCache[order.Currency] = token = await FindTokenAsync(dbContext, network, order.Currency, ct);
                }

                var adopted = await TryAdoptTxHashAsync(order, token, sender, gateway, ct);
                if (adopted == AdoptResult.NoMatch && _clock.Now - order.StatusChangedAt > manualReviewAfter)
                {
                    await MarkFailedAsync(order, WithdrawStatus.Broadcasting,
                        "长期未找到原发送方的交易，无法确认是否出款，需人工核实链上交易");
                }
            }
            catch (Exception ex) when (ex is ChainRpcException or System.Net.Http.HttpRequestException)
            {
                Logger.LogError(ex, "无哈希仲裁单笔异常，跳过本单: OrderId={OrderId}", order.Id);
                if (_clock.Now - order.StatusChangedAt > manualReviewAfter)
                {
                    await MarkFailedAsync(order, WithdrawStatus.Broadcasting,
                        "长期无法查询原发送方的交易，需人工核实是否出款");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "无哈希仲裁单笔异常，跳过本单: OrderId={OrderId}", order.Id);
            }
        }
    }

    protected internal enum AdoptResult
    {
        Adopted,
        NoMatch,
    }

    /// <summary>
    /// 按原发送方、收款地址和净额寻找转账，再用原发送方的 nonce 核对交易。
    /// 转账日志没有 nonce，同地址同金额也可能属于不同单据。哈希未被别单使用且原尝试未变才保存。
    /// </summary>
    private async Task<AdoptResult> TryAdoptTxHashAsync(
        ColWithdrawOrder order, ColToken token, string sender,
        IBscChainGateway gateway, CancellationToken ct)
    {
        // 先核对广播前落库的 ColChainTxAttempts（无需外部浏览器即可认领原尝试）
        if (order.NonceUsed is long usedNonce)
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var attempts = await dbContext.ChainTxAttempts.AsNoTracking()
                .Where(a => a.Purpose == ChainTxPurpose.WithdrawPayout
                            && a.RefId == order.Id
                            && a.Nonce == usedNonce
                            && a.SenderAddress == sender.ToLowerInvariant())
                .OrderByDescending(a => a.AttemptNo)
                .ToListAsync(ct);
            foreach (var attempt in attempts)
            {
                var attemptReceipt = await gateway.GetTransactionReceiptAsync(attempt.TxHash, ct);
                if (attemptReceipt.Status is 0 or 1 && await TryWriteBroadcastHashAsync(order, attempt.TxHash, ct))
                {
                    Logger.LogInformation("提现单 {OrderId} 凭本地签名尝试认领哈希（nonce {Nonce} → {TxHash}）",
                        order.Id, usedNonce, attempt.TxHash);
                    return AdoptResult.Adopted;
                }
            }
        }

        var head = await gateway.GetLatestBlockNumberAsync(ct);
        var fromBlock = Math.Max(0, head - 43_200); // 超出查找窗口的订单保留人工核对路径。
        var page = 1;
        const int maxPages = 10;
        while (!ct.IsCancellationRequested)
        {
            var result = await gateway.GetTransfersForAddressAsync(
                token.ContractAddress, sender, fromBlock, head, page, 100, ct);
            foreach (var log in result.Logs)
            {
                if (!string.Equals(log.From, sender, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(log.To, order.ToAddress, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var scaled = DepositScanManager.TryScaleRaw(log.RawAmount, token.Decimals, out var overflow);
                if (overflow || scaled == null || scaled.Value != order.ActualAmount)
                {
                    continue;
                }

                var logNonce = await gateway.GetTransactionNonceForSenderAsync(log.TxHash, sender, ct);
                if (logNonce == null || logNonce.Value != order.NonceUsed!.Value)
                {
                    Logger.LogDebug("认领候选 {TxHash} nonce {Candidate} ≠ 本单 {Nonce}，跳过", log.TxHash, logNonce, order.NonceUsed);
                    continue;
                }

                var txHash = log.TxHash.ToLowerInvariant();
                if (!await TryWriteBroadcastHashAsync(order, txHash, ct))
                {
                    continue;
                }

                Logger.LogInformation("提现单 {OrderId} 哈希认领成功（nonce {Nonce} → {TxHash}），交回执路径完结",
                    order.Id, order.NonceUsed, txHash);
                return AdoptResult.Adopted;
            }

            if (!result.Incomplete)
            {
                Logger.LogWarning("提现单 {OrderId} 无哈希超时，查询窗口内未找到原发送方的匹配转账",
                    order.Id);
                return AdoptResult.NoMatch;
            }

            page++;
            if (page > maxPages)
            {
                Logger.LogError("{Prefix}提现单 {OrderId} 认领扫描超过 {MaxPages} 页（热钱包出站流水过多），停止本轮扫描",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, order.Id, maxPages);
                return AdoptResult.NoMatch;
            }
        }

        return AdoptResult.NoMatch;
    }

    /// <summary>核对原尝试后清除其广播信息，将订单重新排队并递增重试次数。</summary>
    private async Task<bool> RequeueToProcessingAsync(ColWithdrawOrder order, WithdrawStatus from, string reason, CancellationToken ct)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await WithdrawManager.MatchBroadcastAttempt(dbContext.WithdrawOrders.Where(o => o.Status == from), order)
            .ExecuteUpdateAsync(x => x
                .SetProperty(o => o.Status, WithdrawStatus.Processing)
                .SetProperty(o => o.StatusChangedAt, _clock.Now)
                .SetProperty(o => o.TxHash, (string?)null)
                .SetProperty(o => o.NonceUsed, (long?)null)
                .SetProperty(o => o.BroadcastSenderAddress, (string?)null)
                .SetProperty(o => o.RetryCount, order.RetryCount + 1)
                .SetProperty(o => o.FailReason, reason), ct);
        if (rows > 0)
        {
            Logger.LogWarning("提现单 {OrderId} 回 Processing 重试（第 {Retry} 次）：{Reason}", order.Id, order.RetryCount + 1, reason);
        }

        return rows > 0;
    }

    /// <summary>
    /// 自动退款终态：回执失败或 nonce 被另一笔已完结交易占用 → BroadcastFailed → Rejected，
    /// 净额+手续费自动退回用户可用，告警留痕（可见但不阻塞——不需要人解除状态）。
    /// </summary>
    private async Task AutoRefundAsync(ColWithdrawOrder order, WithdrawStatus from, string reason)
    {
        if (!await MarkFailedAsync(order, from, reason))
        {
            return;
        }
        try
        {
            await _withdraws.RefundAfterApproveAsync(order, auditUserId: null, remark: reason);
            await _alerts.RaiseAsync(null, "提现自动退款",
                $"提现单 {order.Id}：{reason}。净额+手续费已自动退回用户可用余额（系统处置，无需人工）。");
        }
        catch (BusinessException ex) when (ex.Code == CollectionErrorCodes.WithdrawInvalidStatus)
        {
            Logger.LogInformation("提现单 {OrderId} 已由其他操作更新，跳过本次自动退款", order.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 失败状态已经提交，退款仍可由管理端处理。
            Logger.LogError(ex, "{Prefix}提现单 {OrderId} 自动退款失败（单据已转 BroadcastFailed），需人工退款",
                CollectionConsts.AlertPrefixWithdrawHotWallet, order.Id);
            await _alerts.RaiseAsync(null, "提现自动退款失败",
                $"提现单 {order.Id}：{reason}；但退款执行失败（{ex.Message}）。单据已转 BroadcastFailed，请尽快人工退款。");
        }
    }

    /// <summary>交易核对通过后同事务完结订单和出款账本，重复提交相同完结哈希直接返回。</summary>
    public virtual async Task CompleteFromReceiptAsync(
        Guid orderId, string txHash, ColWithdrawOrder? verifiedAttempt = null,
        bool attachVerifiedHash = false, bool allowSiblingAttemptHash = false)
    {
        if (attachVerifiedHash && (verifiedAttempt?.Id != orderId || verifiedAttempt.TxHash != null))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus).WithData("WithdrawOrderId", orderId);
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;
        if (verifiedAttempt != null && !await CanClaimHashAsync(dbContext, verifiedAttempt, txHash))
        {
            // 已成功完结的本单须保持幂等，即使老数据中已有重复哈希。
            var alreadyCompleted = await dbContext.WithdrawOrders.AsNoTracking().AnyAsync(order =>
                order.Id == orderId && order.Status == WithdrawStatus.Completed && order.TxHash == txHash);
            if (!alreadyCompleted)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawReceiptMismatch)
                    .WithData("Reason", "该交易哈希已归属另一笔提现");
            }

            await uow.CompleteAsync();
            return;
        }

        string? expectedHash = attachVerifiedHash ? null : txHash;
        if (allowSiblingAttemptHash && verifiedAttempt != null && verifiedAttempt.TxHash != null
            && !string.Equals(verifiedAttempt.TxHash, txHash, StringComparison.OrdinalIgnoreCase))
        {
            if (!verifiedAttempt.NonceUsed.HasValue)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawReceiptMismatch);
            }

            var siblingValid = await dbContext.ChainTxAttempts.AsNoTracking().AnyAsync(a =>
                a.Purpose == ChainTxPurpose.WithdrawPayout
                && a.RefId == orderId
                && a.Nonce == verifiedAttempt.NonceUsed.Value
                && a.TxHash == txHash);
            if (!siblingValid)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawReceiptMismatch);
            }

            expectedHash = verifiedAttempt.TxHash;
        }

        var eligible = dbContext.WithdrawOrders.Where(o => o.Id == orderId && o.TxHash == expectedHash
            && (o.Status == WithdrawStatus.Broadcasting || o.Status == WithdrawStatus.BroadcastFailed));
        if (verifiedAttempt != null)
        {
            eligible = WithdrawManager.MatchBroadcastAttempt(eligible, verifiedAttempt);
        }

        var rows = await eligible
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Completed)
                .SetProperty(o => o.TxHash, txHash)
                .SetProperty(o => o.StatusChangedAt, now)
                .SetProperty(o => o.CompleteTime, now)
                .SetProperty(o => o.FailReason, (string?)null));
        if (rows == 0)
        {
            // 区分「已完结幂等」与「状态/哈希不符」：静默成功会让管理端补录假成功
            var current = await dbContext.WithdrawOrders.AsNoTracking()
                .FirstAsync(o => o.Id == orderId);
            if (current.Status == WithdrawStatus.Completed && current.TxHash == txHash)
            {
                await uow.CompleteAsync();
                return; // 已被并发完结：幂等返回
            }

            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("Reason", $"状态不符（当前 {current.Status}，哈希 {current.TxHash ?? "无"}）");
        }

        var order = await dbContext.WithdrawOrders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        await _ledger.PostPayoutAsync(order, txHash);
        await uow.CompleteAsync();
        Logger.LogInformation("提现单 {OrderId} 打款完成（{TxHash}）", orderId, txHash);
    }

    private async Task<bool> TryWriteBroadcastHashAsync(ColWithdrawOrder attempt, string txHash, CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        if (!await CanClaimHashAsync(dbContext, attempt, txHash, ct))
        {
            return false;
        }

        var broadcasting = dbContext.WithdrawOrders.Where(order => order.Status == WithdrawStatus.Broadcasting);
        var rows = await WithdrawManager.MatchBroadcastAttempt(broadcasting, attempt)
            .ExecuteUpdateAsync(update => update.SetProperty(order => order.TxHash, txHash), ct);
        await uow.CompleteAsync(ct);
        return rows > 0;
    }

    private static async Task<bool> CanClaimHashAsync(
        CollectionDbContext dbContext, ColWithdrawOrder attempt, string txHash, CancellationToken ct = default)
    {
        var identity = $"{attempt.NetworkName}:{txHash}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('Collection:WithdrawalHash'), hashtext({identity}))", ct);
        return !await dbContext.WithdrawOrders.AsNoTracking().AnyAsync(order => order.Id != attempt.Id
            && order.NetworkName == attempt.NetworkName && order.TxHash == txHash, ct);
    }

    // ── ② 小额自动审 ─────────────────────────────────────────────────────────────────────

    public virtual Task AutoApproveSmallOrdersAsync(
        ColNetwork network, IBscChainGateway gateway, int batchSize, CancellationToken ct = default)
    {
        return WithDbScopeAsync(() => AutoApproveCoreAsync(network, gateway, batchSize, ct));
    }

    private async Task AutoApproveCoreAsync(
        ColNetwork network, IBscChainGateway gateway, int batchSize, CancellationToken ct)
    {
        var threshold = await _settings.GetDecimalAsync(CollectionSettings.Withdraw.AutoApproveMaxAmount, WithdrawDefaults.AutoApproveMaxAmount);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var pending = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Pending && o.Amount <= threshold && o.NetworkName == network.Name)
            .OrderBy(o => o.CreationTime)
            .Take(batchSize)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return;
        }

        if (await IsHaltedAsync())
        {
            return; // 总闸：整批等下一轮
        }

        // 审核前按币种检查本批次的净额合计，实际广播时再次逐单检查。
        var wallet = await _guard.LoadAsync(ct);
        if (wallet == null)
        {
            Logger.LogError("{Prefix}提现热钱包未配置，小额自动审停转", CollectionConsts.AlertPrefixWithdrawHotWallet);
            return;
        }

        var tokenCache = new System.Collections.Generic.Dictionary<CollectionCurrency, ColToken>();
        foreach (var g in pending.GroupBy(o => o.Currency))
        {
            tokenCache[g.Key] = await FindTokenAsync(dbContext, network, g.Key, ct);
            await _guard.EnsureCanPayAsync(wallet, gateway, tokenCache[g.Key], g.Sum(o => o.ActualAmount), ct);
        }

        foreach (var order in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _withdraws.ApproveAsync(order.Id, null, "系统自动审核通过（小额）");
            }
            catch (BusinessException ex) when (ex.Code == CollectionErrorCodes.WithdrawBreakerOpen
                                               || ex.Code == CollectionErrorCodes.HotWalletInsolvent
                                               || ex.Code == CollectionErrorCodes.HotWalletGasShort)
            {
                Logger.LogWarning("提现单 {OrderId} 小额自动审被护栏拦下（{Code}），本轮停止自动审", order.Id, ex.Code);
                break;
            }
            catch (BusinessException ex)
            {
                // 单笔业务拒绝（InvalidStatus/余额两桶异常等）：记日志继续下一单
                Logger.LogWarning(ex, "提现单 {OrderId} 小额自动审被拒（{Code}）", order.Id, ex.Code);
            }
        }
    }

    // ── ③ 广播队列 ───────────────────────────────────────────────────────────────────────

    public virtual Task BroadcastQueueAsync(
        ColNetwork network, IBscChainGateway gateway, int batchSize, CancellationToken ct = default)
    {
        return WithDbScopeAsync(() => BroadcastQueueCoreAsync(network, gateway, batchSize, ct));
    }

    private async Task BroadcastQueueCoreAsync(
        ColNetwork network, IBscChainGateway gateway, int batchSize, CancellationToken ct)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var queue = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Processing && o.NetworkName == network.Name)
            .OrderBy(o => o.CreationTime)
            .Take(batchSize)
            .ToListAsync(ct);
        if (queue.Count == 0)
        {
            return;
        }

        if (await IsHaltedAsync())
        {
            return; // 总闸：整批等下一轮
        }

        // 复用本轮钱包和代币配置。前一笔交易可能影响余额，广播前仍逐单检查。
        var wallet = await _guard.LoadAsync(ct)
                     ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
        var tokenCache = new System.Collections.Generic.Dictionary<CollectionCurrency, ColToken>();
        foreach (var g in queue.Select(o => o.Currency).Distinct())
        {
            tokenCache[g] = await FindTokenAsync(dbContext, network, g, ct);
        }

        foreach (var order in queue)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await BroadcastOneAsync(network, gateway, wallet, tokenCache, order, ct);
            }
            catch (BusinessException ex) when (ex.Code == CollectionErrorCodes.WithdrawBreakerOpen
                                               || ex.Code == CollectionErrorCodes.HotWalletInsolvent
                                               || ex.Code == CollectionErrorCodes.HotWalletGasShort)
            {
                Logger.LogWarning("提现单 {OrderId} 广播被护栏拦下（{Code}），本轮停止广播", order.Id, ex.Code);
                break;
            }
            catch (Exception ex)
            {
                // 发送前可能已提交广播尝试，后续由恢复流程按已保存的状态核对。
                Logger.LogError(ex, "提现单 {OrderId} 广播处理异常，后续按单据状态继续处理", order.Id);
            }
        }
    }

    private async Task BroadcastOneAsync(
        ColNetwork network, IBscChainGateway gateway, WithdrawHotWallet wallet,
        System.Collections.Generic.Dictionary<CollectionCurrency, ColToken> tokenCache,
        ColWithdrawOrder order, CancellationToken ct)
    {
        var token = tokenCache[order.Currency];
        var sender = wallet.Address.ToLowerInvariant();

        // 广播前偿付/熔断/BNB 检查（签名临界区内还有最后一道总闸检查）
        await _guard.EnsureCanPayAsync(wallet, gateway, token, order.ActualAmount, ct);

        await SignGate.WaitAsync(ct);
        try
        {
            if (await IsHaltedAsync())
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawHalted);
            }

            var masterKey = await _chainKeyProvider.GetAsync()
                            ?? throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);

            var privateKey = wallet.DecryptKey(masterKey, _encryptor);
            var derived = new Nethereum.Signer.EthECKey(privateKey).GetPublicAddress();
            if (!string.Equals(derived, sender, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured)
                    .WithData("Reason", "提现钱包地址与签名密钥不匹配");
            }

            var latestNonce = await gateway.GetTransactionCountLatestAsync(sender, ct);
            var pendingNonce = await gateway.GetTransactionCountAsync(sender, ct);
            var networkGasWei = await gateway.GetGasPriceWeiAsync(ct);
            var desiredGasWei = BscGasPricePolicy.ApplyBump(networkGasWei, order.RetryCount);
            var hotBnbWei = BscGasPricePolicy.EtherToWei(await gateway.GetBnbBalanceAsync(sender, ct));
            var affordableGasWei = BscGasPricePolicy.CapToAffordable(
                desiredGasWei, hotBnbWei, BscGasPricePolicy.Erc20TransferGasLimit);
            if (affordableGasWei <= 0 || desiredGasWei > affordableGasWei)
            {
                await _alerts.RaiseAsync(null, "提现热钱包 BNB 不足",
                    $"热钱包 {sender} 当前 BNB 不足以按期望 gasPrice={desiredGasWei} wei 广播提现单 {order.Id}（RetryCount={order.RetryCount}），已停止广播（拒绝静默压价）。");
                throw new BusinessException(CollectionErrorCodes.HotWalletGasShort)
                    .WithData("DesiredGasPriceWei", desiredGasWei.ToString(CultureInfo.InvariantCulture))
                    .WithData("AffordableGasPriceWei", affordableGasWei.ToString(CultureInfo.InvariantCulture));
            }

            var dataHex = SweepManager.BuildErc20TransferData(order.ToAddress, order.ActualAmount, token.Decimals);
            long nonce;
            SignedChainTransaction signed;
            string txHash;
            using (var claimUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var claimDb = await _dbContextProvider.GetDbContextAsync();
                await ChainTxSlotCoordinator.LockAccountSlotAsync(claimDb, token.NetworkId, sender, ct);
                nonce = await ChainTxSlotCoordinator.AllocateSerialNonceAsync(
                    claimDb, token.NetworkId, network.Name, sender, latestNonce, pendingNonce, ct);

                var claimNow = _clock.Now;
                var queued = claimDb.WithdrawOrders.Where(o => o.Status == WithdrawStatus.Processing);
                var claimed = await WithdrawManager.MatchBroadcastAttempt(queued, order)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, WithdrawStatus.Broadcasting)
                        .SetProperty(o => o.StatusChangedAt, claimNow)
                        .SetProperty(o => o.NonceUsed, nonce)
                        .SetProperty(o => o.BroadcastSenderAddress, sender)
                        .SetProperty(o => o.BroadcastAt, claimNow)
                        .SetProperty(o => o.FailReason, (string?)null), ct);
                if (claimed == 0)
                {
                    return;
                }

                signed = await gateway.PrepareTransactionAsync(
                    privateKey, token.ContractAddress, dataHex, "0x0", network.ChainId, nonce, desiredGasWei, ct);
                txHash = signed.Hash.ToLowerInvariant();
                if (!await claimDb.ChainTxAttempts.AsNoTracking().AnyAsync(a => a.NetworkId == token.NetworkId && a.TxHash == txHash, ct))
                {
                    var attemptId = LazyServiceProvider != null ? GuidGenerator.Create() : Guid.NewGuid();
                    claimDb.ChainTxAttempts.Add(new ColChainTxAttempt(attemptId)
                    {
                        NetworkId = token.NetworkId,
                        Purpose = ChainTxPurpose.WithdrawPayout,
                        RefId = order.Id,
                        SenderAddress = sender,
                        ToAddress = token.ContractAddress.ToLowerInvariant(),
                        Nonce = nonce,
                        AttemptNo = order.RetryCount + 1,
                        TxHash = txHash,
                        GasPriceWei = desiredGasWei.ToString(CultureInfo.InvariantCulture),
                        GasLimit = BscGasPricePolicy.Erc20TransferGasLimit,
                        RawSignedTx = signed.RawHex,
                        Status = ChainTxAttemptStatus.Signed,
                        BroadcastAt = claimNow,
                        CreationTime = claimNow,
                    });
                    await claimDb.SaveChangesAsync(ct);
                }

                await claimUow.CompleteAsync(ct);
            }

            try
            {
                await gateway.BroadcastTransactionAsync(signed, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ChainTxSlotCoordinator.IsDeterministicTxRejection(ex))
                {
                    await MarkWithdrawAttemptStatusAsync(token.NetworkId, txHash, ChainTxAttemptStatus.Rejected, ct);
                }

                throw;
            }

            await MarkWithdrawAttemptStatusAsync(token.NetworkId, txHash, ChainTxAttemptStatus.Broadcasted, ct);
            order.NonceUsed = nonce;
            order.BroadcastSenderAddress = sender;
            if (!await TryWriteBroadcastHashAsync(order, txHash, ct))
            {
                Logger.LogWarning("{Prefix}提现单 {OrderId} 已发送，但未能关联交易哈希 {TxHash}，等待核实",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, order.Id, txHash);
                return;
            }

            Logger.LogInformation("提现单 {OrderId} 已广播 {Actual} {Currency} → {To}（{TxHash}, Nonce={Nonce}, RetryCount={Retry}, GasWei={GasWei}）",
                order.Id, order.ActualAmount, order.Currency, order.ToAddress, txHash, nonce, order.RetryCount, desiredGasWei);
        }
        finally
        {
            SignGate.Release();
        }
    }

    private async Task TryReplaceStuckWithdrawHeadAsync(
        ColNetwork network, IBscChainGateway gateway, ColWithdrawOrder order,
        List<ColChainTxAttempt> attempts, Dictionary<CollectionCurrency, ColToken> tokenCache, CancellationToken ct)
    {
        if (await IsHaltedAsync()
            || !WithdrawManager.HasBroadcastSender(order)
            || !order.BroadcastAt.HasValue)
        {
            return;
        }

        var now = _clock.Now;
        var lastActiveAttemptAt = attempts
            .Where(a => a.Status != ChainTxAttemptStatus.Rejected)
            .Select(a => (DateTime?)a.BroadcastAt)
            .FirstOrDefault() ?? order.BroadcastAt.Value;
        var lastWasRejected = attempts.Count > 0 && attempts[0].Status == ChainTxAttemptStatus.Rejected;
        if (!lastWasRejected && now - lastActiveAttemptAt < WithdrawHeadReplaceAfter)
        {
            return;
        }
        if (now - order.BroadcastAt.Value < WithdrawHeadReplaceAfter)
        {
            return;
        }

        if (attempts.Count >= BscGasPricePolicy.MaxBumpSteps + 1)
        {
            return;
        }

        var wallet = await _guard.LoadAsync(ct);
        var sender = order.BroadcastSenderAddress!;
        if (wallet == null || !string.Equals(wallet.Address, sender, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var nonce = order.NonceUsed!.Value;
        var latestNonce = await gateway.GetTransactionCountLatestAsync(sender, ct);
        if (latestNonce != nonce)
        {
            return;
        }

        if (!tokenCache.TryGetValue(order.Currency, out var token))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            tokenCache[order.Currency] = token = await FindTokenAsync(dbContext, network, order.Currency, ct);
        }

        await SignGate.WaitAsync(ct);
        try
        {
            if (await IsHaltedAsync())
            {
                return;
            }

            var masterKey = await _chainKeyProvider.GetAsync();
            if (masterKey == null)
            {
                return;
            }

            var privateKey = wallet.DecryptKey(masterKey, _encryptor);
            var derived = new Nethereum.Signer.EthECKey(privateKey).GetPublicAddress();
            if (!string.Equals(derived, sender, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var networkGasWei = await gateway.GetGasPriceWeiAsync(ct);
            var maxAttemptNo = attempts.Count > 0 ? attempts.Max(a => a.AttemptNo) : Math.Max(1, order.RetryCount + 1);
            var bumpSteps = Math.Min(BscGasPricePolicy.MaxBumpSteps, Math.Max(1, maxAttemptNo));
            var prevGasWei = (await gateway.GetTransactionGasPriceWeiAsync(order.TxHash!, ct)) ?? System.Numerics.BigInteger.Zero;
            foreach (var a in attempts)
            {
                var aWei = BscGasPricePolicy.ParseWeiOrZero(a.GasPriceWei);
                if (aWei > prevGasWei)
                {
                    prevGasWei = aWei;
                }
            }

            var desiredGasWei = BscGasPricePolicy.ReplacementGasWei(
                BscGasPricePolicy.ApplyBump(networkGasWei, bumpSteps), prevGasWei);
            var hotBnbWei = BscGasPricePolicy.EtherToWei(await gateway.GetBnbBalanceAsync(sender, ct));
            var affordableGasWei = BscGasPricePolicy.CapToAffordable(
                desiredGasWei, hotBnbWei, BscGasPricePolicy.Erc20TransferGasLimit);
            if (affordableGasWei <= 0 || desiredGasWei > affordableGasWei)
            {
                await _alerts.RaiseAsync(null, "提现热钱包 BNB 不足无法加速",
                    $"热钱包 {sender} 当前 BNB 不足以对提现单 {order.Id}（nonce={nonce}）按 {desiredGasWei} wei 同 nonce 加速，请补充 BNB。");
                return;
            }

            var dataHex = SweepManager.BuildErc20TransferData(order.ToAddress, order.ActualAmount, token.Decimals);
            SignedChainTransaction replacement;
            string replaceHash;
            var oldHash = order.TxHash!;
            bool reserved;
            using (var replaceUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var replaceDb = await _dbContextProvider.GetDbContextAsync();
                await ChainTxSlotCoordinator.LockAccountSlotAsync(replaceDb, token.NetworkId, sender, ct);
                var stillBroadcasting = await WithdrawManager.MatchBroadcastAttempt(
                        replaceDb.WithdrawOrders.Where(o => o.Status == WithdrawStatus.Broadcasting), order)
                    .AnyAsync(ct);
                if (!stillBroadcasting)
                {
                    return;
                }

                replacement = await gateway.PrepareTransactionAsync(
                    privateKey, token.ContractAddress, dataHex, "0x0", network.ChainId, nonce, desiredGasWei, ct);
                replaceHash = replacement.Hash.ToLowerInvariant();
                if (!await replaceDb.ChainTxAttempts.AsNoTracking().AnyAsync(a => a.NetworkId == token.NetworkId && a.TxHash == replaceHash, ct))
                {
                    var attemptId = LazyServiceProvider != null ? GuidGenerator.Create() : Guid.NewGuid();
                    replaceDb.ChainTxAttempts.Add(new ColChainTxAttempt(attemptId)
                    {
                        NetworkId = token.NetworkId,
                        Purpose = ChainTxPurpose.WithdrawPayout,
                        RefId = order.Id,
                        SenderAddress = sender,
                        ToAddress = token.ContractAddress.ToLowerInvariant(),
                        Nonce = nonce,
                        AttemptNo = maxAttemptNo + 1,
                        TxHash = replaceHash,
                        GasPriceWei = desiredGasWei.ToString(CultureInfo.InvariantCulture),
                        GasLimit = BscGasPricePolicy.Erc20TransferGasLimit,
                        RawSignedTx = replacement.RawHex,
                        Status = ChainTxAttemptStatus.Signed,
                        BroadcastAt = now,
                        CreationTime = now,
                    });
                    await replaceDb.SaveChangesAsync(ct);
                }

                reserved = true;
                await replaceUow.CompleteAsync(ct);
            }

            if (!reserved)
            {
                return;
            }

            try
            {
                await gateway.BroadcastTransactionAsync(replacement, ct);
                using var commitUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var commitDb = await _dbContextProvider.GetDbContextAsync();
                await ChainTxSlotCoordinator.LockAccountSlotAsync(commitDb, token.NetworkId, sender, ct);
                var updated = await WithdrawManager.MatchBroadcastAttempt(
                        commitDb.WithdrawOrders.Where(o => o.Status == WithdrawStatus.Broadcasting), order)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.TxHash, replaceHash)
                        .SetProperty(o => o.BroadcastAt, now), ct);
                if (updated > 0)
                {
                    await commitDb.ChainTxAttempts
                        .Where(a => a.NetworkId == token.NetworkId && a.TxHash == replaceHash)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ChainTxAttemptStatus.Broadcasted), ct);
                    await commitDb.ChainTxAttempts
                        .Where(a => a.NetworkId == token.NetworkId && a.TxHash == oldHash)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.Status, ChainTxAttemptStatus.Replaced)
                            .SetProperty(a => a.RawSignedTx, (string?)null), ct);
                }
                await commitUow.CompleteAsync(ct);
                Logger.LogWarning(
                    "提现单 {OrderId} 队头超时，已同 nonce={Nonce} 抬价替换（{OldHash} -> {NewHash}, GasWei={GasWei}）",
                    order.Id, nonce, oldHash, replaceHash, desiredGasWei);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ChainTxSlotCoordinator.IsDeterministicTxRejection(ex))
                {
                    await MarkWithdrawAttemptStatusAsync(token.NetworkId, replaceHash, ChainTxAttemptStatus.Rejected, ct);
                }
                Logger.LogWarning(ex, "提现单 {OrderId} 同 nonce 加速广播异常（{Hash}），保留原单待下轮重抬档", order.Id, replaceHash);
            }
        }
        finally
        {
            SignGate.Release();
        }
    }

    // ── 管理端补救路径（BroadcastFailed 单）─────────────────────────────────────────────

    /// <summary>
    /// 处置 BroadcastFailed：成功回执直接完结；只有重新排队受重试次数限制。
    /// 无哈希时仅接受同一发送方的 nonce 已被另一笔完结提现占用的证据。
    /// </summary>
    public virtual async Task RetryAsync(Guid orderId, ColNetwork network, IBscChainGateway gateway, CancellationToken ct = default)
    {
        await WithDbScopeAsync(async () =>
        {
            await RetryCoreAsync(orderId, network, gateway, ct);
        });
    }

    private async Task RetryCoreAsync(Guid orderId, ColNetwork network, IBscChainGateway gateway, CancellationToken ct)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = await dbContext.WithdrawOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new BusinessException(CollectionErrorCodes.WithdrawNotFound);
        if (order.Status != WithdrawStatus.BroadcastFailed)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("From", order.Status.ToString()).WithData("To", "Processing");
        }

        if (order.NetworkName != network.Name)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawNetworkUnsupported);
        }

        var txHash = order.TxHash;
        if (!string.IsNullOrEmpty(txHash))
        {
            var receipt = await gateway.GetTransactionReceiptAsync(txHash, ct);
            if (receipt.Status is not (0 or 1))
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                    .WithData("TxHash", txHash);
            }

            if (receipt.Status == 1)
            {
                await CompleteFromReceiptAsync(order.Id, txHash, order);
                return;
            }
        }

        var maxRetries = await _settings.GetIntAsync(CollectionSettings.Withdraw.MaxRetries, WithdrawDefaults.MaxRetries);
        if (order.RetryCount >= maxRetries)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawRetryLimitReached)
                .WithData("RetryCount", order.RetryCount).WithData("Max", maxRetries);
        }

        if (!string.IsNullOrEmpty(txHash))
        {
            // 回执失败：清哈希回 Processing（新 nonce 重试）
            await RequeueManualRetryAsync(order, "管理端重试（回执失败）", ct);
            return;
        }

        if (!await _withdraws.HasOtherCompletedAttemptAsync(order, ct))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                .WithData("Reason", "缺少原发送方的链上未出款证据，请先核实交易");
        }

        await RequeueManualRetryAsync(order, "管理端重试（原 nonce 已被另一笔提现占用）", ct);
    }

    private async Task RequeueManualRetryAsync(ColWithdrawOrder order, string reason, CancellationToken ct)
    {
        if (!await RequeueToProcessingAsync(order, WithdrawStatus.BroadcastFailed, reason, ct))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("WithdrawOrderId", order.Id);
        }
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    private async Task<ColToken> FindTokenAsync(CollectionDbContext dbContext, ColNetwork network, CollectionCurrency currency, CancellationToken ct)
    {
        var symbol = CollectionLedgerPoster.CurrencyCode(currency);
        return await dbContext.Tokens.AsNoTracking()
                   .FirstOrDefaultAsync(t => t.NetworkId == network.Id && t.Symbol == symbol, ct)
               ?? throw new Volo.Abp.AbpException($"网络 {network.Name} 缺少代币 {symbol}（种子未建或被禁用）");
    }

    private async Task<bool> MarkFailedAsync(ColWithdrawOrder order, WithdrawStatus from, string reason)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await WithdrawManager.MatchBroadcastAttempt(dbContext.WithdrawOrders.Where(o => o.Status == from), order)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.BroadcastFailed)
                .SetProperty(o => o.StatusChangedAt, _clock.Now)
                .SetProperty(o => o.FailReason, reason));
        if (rows > 0)
        {
            Logger.LogError("{Prefix}提现单 {OrderId} → BroadcastFailed：{Reason}",
                CollectionConsts.AlertPrefixWithdrawHotWallet, order.Id, reason);
        }

        return rows > 0;
    }

    private async Task<bool> IsHaltedAsync()
    {
        return await _settings.IsTrueAsync(CollectionSettings.Withdraw.Halt);
    }

    private async Task MarkWithdrawAttemptStatusAsync(
        Guid networkId, string txHash, ChainTxAttemptStatus status, CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var normalizedHash = txHash.ToLowerInvariant();
        var clearRaw = status is ChainTxAttemptStatus.Confirmed or ChainTxAttemptStatus.Reverted or ChainTxAttemptStatus.Replaced or ChainTxAttemptStatus.Rejected;
        var query = dbContext.ChainTxAttempts.Where(a => a.NetworkId == networkId && a.TxHash == normalizedHash);
        if (clearRaw)
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.RawSignedTx, (string?)null), ct);
        }
        else
        {
            await query.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status), ct);
        }

        await uow.CompleteAsync(ct);
    }

}
