using AbpAdmin.Collection.Ledger;
using System;
using System.Collections.Generic;
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
/// 每轮先查在途交易，再处理小额自动审核和待广播订单。发送前保存原发送地址和 nonce。
/// 有哈希的订单每轮查回执，无哈希的订单超时后按原发送方与 nonce 核对。
/// 只有失败回执或另一笔已完结提现占用同一发送方的 nonce，才自动重试或退款。
/// </summary>
public class WithdrawBroadcastManager : DomainService
{
    /// <summary>签名临界区（进程内串行；跨实例由 worker 的 Redis 轮锁兜底）。</summary>
    private static readonly SemaphoreSlim SignGate = new(1, 1);

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

        // ① 有哈希的 Broadcasting：每轮查回执（正常完结路径就在这，不等超时）
        var withHash = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Broadcasting && o.TxHash != null && o.NetworkName == network.Name)
            .OrderBy(o => o.StatusChangedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var order in withHash)
        {
            try
            {
                var receipt = await gateway.GetTransactionReceiptAsync(order.TxHash!, ct);
                if (receipt.Status is not (0 or 1))
                {
                    if (_clock.Now - order.StatusChangedAt > TimeSpan.FromMinutes(timeoutMinutes * 3))
                    {
                        await MarkFailedAsync(order, WithdrawStatus.Broadcasting, "已记录交易哈希但长期未获取回执，需核实链上结果");
                    }

                    continue;
                }

                if (receipt.Status == 1)
                {
                    await CompleteFromReceiptAsync(order.Id, order.TxHash!, order);
                }
                else if (order.RetryCount + 1 >= maxRetries)
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
        Guid orderId, string txHash, ColWithdrawOrder? verifiedAttempt = null, bool attachVerifiedHash = false)
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

        var expectedHash = attachVerifiedHash ? null : txHash;
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

            var nonce = await gateway.GetTransactionCountAsync(sender, ct);
            if (!await ClaimBroadcastAttemptAsync(order, sender, nonce, ct))
            {
                return;
            }

            var dataHex = SweepManager.BuildErc20TransferData(order.ToAddress, order.ActualAmount, token.Decimals);
            var txHash = (await gateway.SendTransactionAsync(
                privateKey, token.ContractAddress, dataHex, "0x0", network.ChainId, nonce, ct)).ToLowerInvariant();
            order.NonceUsed = nonce;
            order.BroadcastSenderAddress = sender;
            if (!await TryWriteBroadcastHashAsync(order, txHash, ct))
            {
                Logger.LogWarning("{Prefix}提现单 {OrderId} 已发送，但未能关联交易哈希 {TxHash}，等待核实",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, order.Id, txHash);
                return;
            }

            Logger.LogInformation("提现单 {OrderId} 已广播 {Actual} {Currency} → {To}（{TxHash}）",
                order.Id, order.ActualAmount, order.Currency, order.ToAddress, txHash);
        }
        finally
        {
            SignGate.Release();
        }
    }

    private async Task<bool> ClaimBroadcastAttemptAsync(ColWithdrawOrder order, string sender, long nonce, CancellationToken ct)
    {
        // 发送结果可能未知，必须先提交发送地址和 nonce，才能在恢复时识别原尝试。
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;
        var queued = dbContext.WithdrawOrders.Where(o => o.Status == WithdrawStatus.Processing);
        var rows = await WithdrawManager.MatchBroadcastAttempt(queued, order)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Broadcasting)
                .SetProperty(o => o.StatusChangedAt, now)
                .SetProperty(o => o.NonceUsed, nonce)
                .SetProperty(o => o.BroadcastSenderAddress, sender)
                .SetProperty(o => o.BroadcastAt, now)
                .SetProperty(o => o.FailReason, (string?)null), ct);
        await uow.CompleteAsync(ct);
        return rows > 0;
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

}
