using System;
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
/// 提现广播轮次逻辑（M3）：卡单恢复 → 小额自动审 → 广播队列。
/// 与 SweepManager 同款纪律：AsNoTracking 读 + 守卫式 ExecuteUpdate 写、一动作一事务。
/// 广播失败语义（对齐 exchange）：
///  · Broadcasting 超时且有哈希 → 查回执：成功完结 / 失败重试（超限转 BroadcastFailed）
///  · Broadcasting 超时且无哈希 → BroadcastFailed「需人工核查」，绝不盲目重播（双花防线）
///  · 签名/发送抛异常 → 保持 Broadcasting（无哈希），交由卡单恢复人工核查——发送结果未知时宁可冻结也不重发
/// nonce 每次广播取热钱包 pending nonce（不缓存复用），杜绝替换重签双花。
/// </summary>
public class WithdrawBroadcastManager : DomainService
{
    /// <summary>签名临界区（进程内串行；跨实例由 worker 的 Redis 轮锁兜底）。</summary>
    private static readonly SemaphoreSlim SignGate = new(1, 1);

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settings;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly WithdrawManager _withdraws;
    private readonly WithdrawLedgerPoster _ledger;
    private readonly WithdrawGuard _guard;
    private readonly IClock _clock;
    private readonly Volo.Abp.Security.Encryption.IStringEncryptionService _encryptor;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly ChainAlertService _alerts;

    public WithdrawBroadcastManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings,
        IUnitOfWorkManager unitOfWorkManager,
        WithdrawManager withdraws,
        WithdrawLedgerPoster ledger,
        WithdrawGuard guard,
        IClock clock,
        Volo.Abp.Security.Encryption.IStringEncryptionService encryptor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
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
        _configuration = configuration;
        _alerts = alerts;
    }

    /// <summary>一轮：卡单恢复 → 小额自动审 → 广播队列。任一段失败不挡下一段。</summary>
    public virtual async Task RunRoundAsync(
        ColNetwork network, IBscChainGateway gateway,
        int autoApproveBatch, int broadcastBatch, CancellationToken ct = default)
    {
        // 直调场景（测试/管理端）没有环境 UoW：自开非事务 UoW 供 DbContext 解析；
        // worker 调用自带 UoW 则加入。原子块（完结/审批）内部自开 requiresNew 事务。
        await WithDbScopeAsync(async () =>
        {
            await RecoverStuckBroadcastingAsync(network, gateway, ct);
            await AutoApproveSmallOrdersAsync(network, gateway, autoApproveBatch, ct);
            await BroadcastQueueAsync(network, gateway, broadcastBatch, ct);
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
        var timeoutMinutes = await GetIntAsync(CollectionSettings.Withdraw.BroadcastTimeoutMinutes, WithdrawDefaults.BroadcastTimeoutMinutes);
        var maxRetries = await GetIntAsync(CollectionSettings.Withdraw.MaxRetries, WithdrawDefaults.MaxRetries);
        var cutoff = _clock.Now.AddMinutes(-timeoutMinutes);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // ① 有哈希的 Broadcasting：每轮查回执（正常完结路径就在这，不等超时）
        var withHash = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Broadcasting && o.TxHash != null)
            .OrderBy(o => o.StatusChangedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var order in withHash)
        {
            ct.ThrowIfCancellationRequested();
            var receipt = await gateway.GetTransactionReceiptAsync(order.TxHash!, ct);
            if (receipt.Status == null)
            {
                continue; // 链上仍未定：继续等
            }

            if (receipt.Status == 1)
            {
                await CompleteFromReceiptAsync(order.Id, order.TxHash!);
            }
            else if (order.RetryCount + 1 >= maxRetries)
            {
                // 回执失败（revert）= 链上可证未出款：自动退款终态，不等人
                await AutoRefundAsync(order, WithdrawStatus.Broadcasting,
                    $"链上回执失败且重试次数用尽（RetryCount={order.RetryCount}），自动退款");
            }
            else
            {
                await RequeueAfterFailedReceiptAsync(order, "链上回执失败，重试", ct);
            }
        }

        // ② 无哈希且超时的 Broadcasting：nonce 自动仲裁（无人工）。
        // 判据 = 热钱包 pending nonce vs 发送前落库的 NonceUsed：
        //   pending ≤ NonceUsed → 本单交易从未进入 mempool（或已被丢弃）→ 可证未出款
        //     → 未超限自动回 Processing 重播；超限自动退款
        //   pending > NonceUsed → 交易在途/已挖（占用了该 nonce）→ 按热钱包出站转账
        //     （from=热钱包, to=收款地址, 金额=净额）从 Etherscan 扫回哈希认领，交给回执路径；
        //     挖出会被扫到、被节点丢弃则 pending 回落到上一分支——两种结局都自动收敛
        var stuckNoHash = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Broadcasting && o.TxHash == null && o.StatusChangedAt < cutoff)
            .OrderBy(o => o.StatusChangedAt)
            .Take(20)
            .ToListAsync(ct);

        var wallet = await _guard.LoadAsync(ct);
        if (wallet == null)
        {
            if (stuckNoHash.Count > 0)
            {
                Logger.LogError("{Prefix}{Count} 笔无哈希超时单因热钱包未配置无法仲裁，等待配置恢复",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, stuckNoHash.Count);
            }

            return;
        }

        foreach (var order in stuckNoHash)
        {
            ct.ThrowIfCancellationRequested();
            if (order.NonceUsed == null)
            {
                // 理论不可达（nonce 发送前落库）：防御分支——历史脏数据转失败并告警，不重播
                Logger.LogError("{Prefix}提现单 {OrderId} 无哈希且无 NonceUsed（脏数据），转 BroadcastFailed",
                    CollectionConsts.AlertPrefixWithdrawHotWallet, order.Id);
                await MarkFailedAsync(order.Id, WithdrawStatus.Broadcasting, "无哈希且无 NonceUsed（数据异常）");
                continue;
            }

            var token = await FindTokenAsync(dbContext, wallet.Network, order.Currency, ct);
            var pendingNonce = await gateway.GetTransactionCountAsync(wallet.Address, ct);
            if (pendingNonce <= order.NonceUsed.Value)
            {
                // 可证未出款：重播安全（新 nonce）
                if (order.RetryCount + 1 >= maxRetries)
                {
                    await AutoRefundAsync(order, WithdrawStatus.Broadcasting,
                        $"广播超时且链上可证未出款（pending nonce {pendingNonce} ≤ NonceUsed {order.NonceUsed}），重试次数用尽，自动退款");
                }
                else
                {
                    var requeued = await dbContext.WithdrawOrders
                        .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.Broadcasting
                                    && o.TxHash == null && o.NonceUsed == order.NonceUsed)
                        .ExecuteUpdateAsync(x => x
                            .SetProperty(o => o.Status, WithdrawStatus.Processing)
                            .SetProperty(o => o.StatusChangedAt, _clock.Now)
                            .SetProperty(o => o.NonceUsed, (long?)null)
                            .SetProperty(o => o.RetryCount, order.RetryCount + 1)
                            .SetProperty(o => o.FailReason,
                                $"广播超时未上链（nonce 未被占用，可证未出款），自动重播（第 {order.RetryCount + 1} 次）"), ct);
                    if (requeued > 0)
                    {
                        Logger.LogWarning("提现单 {OrderId} 无哈希超时：pending nonce {Pending} ≤ NonceUsed {Used}，可证未出款，回 Processing 自动重播",
                            order.Id, pendingNonce, order.NonceUsed);
                    }
                }
            }
            else
            {
                // 交易在途/已挖：找回哈希认领（找不到＝仍在 mempool 或索引滞后，下一轮再看）
                await TryAdoptTxHashAsync(order, token, wallet, gateway, ct);
            }
        }
    }

    /// <summary>
    /// 哈希认领：按热钱包出站转账（from=热钱包、to=收款地址、金额=净额）在近窗口内找回
    /// 本单交易哈希并落库，交给正常回执确认路径完结。CAS 只在仍无哈希且 nonce 未变时生效。
    /// </summary>
    private async Task TryAdoptTxHashAsync(
        ColWithdrawOrder order, ColToken token, WithdrawHotWallet wallet,
        IBscChainGateway gateway, CancellationToken ct)
    {
        var head = await gateway.GetLatestBlockNumberAsync(ct);
        var fromBlock = Math.Max(0, head - 43_200); // ≈9 小时（BSC 0.75s/块），覆盖超时阈值 + 缓冲
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var page = 1;
        while (!ct.IsCancellationRequested)
        {
            var result = await gateway.GetTransfersForAddressAsync(
                token.ContractAddress, wallet.Address, fromBlock, head, page, 100, ct);
            foreach (var log in result.Logs)
            {
                if (!string.Equals(log.From, wallet.Address, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(log.To, order.ToAddress, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var scaled = DepositScanManager.TryScaleRaw(log.RawAmount, token.Decimals, out var overflow);
                if (overflow || scaled == null || scaled.Value != order.ActualAmount)
                {
                    continue;
                }

                var rows = await dbContext.WithdrawOrders
                    .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.Broadcasting
                                && o.TxHash == null && o.NonceUsed == order.NonceUsed)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.TxHash, log.TxHash), ct);
                if (rows > 0)
                {
                    Logger.LogInformation("提现单 {OrderId} 哈希认领成功（nonce {Nonce} → {TxHash}），交回执路径完结",
                        order.Id, order.NonceUsed, log.TxHash);
                    return;
                }

                return; // CAS 未中：已被并发处置
            }

            if (!result.Incomplete)
            {
                Logger.LogWarning("提现单 {OrderId} 无哈希超时：pending nonce 已越过 NonceUsed（交易在途/已挖）但窗口内未扫到匹配转账，下一轮再仲裁",
                    order.Id);
                return;
            }

            page++;
        }
    }

    /// <summary>
    /// 自动退款终态：链上可证未出款（revert / nonce 未占用）→ BroadcastFailed → Rejected，
    /// 净额+手续费自动退回用户可用，告警留痕（可见但不阻塞——不需要人解除状态）。
    /// </summary>
    private async Task AutoRefundAsync(ColWithdrawOrder order, WithdrawStatus from, string reason)
    {
        await MarkFailedAsync(order.Id, from, reason);
        await _withdraws.RefundAfterApproveAsync(order.Id, auditUserId: null, remark: reason);
        await _alerts.RaiseAsync(null, "提现自动退款",
            $"提现单 {order.Id}：{reason}。净额+手续费已自动退回用户可用余额（系统处置，无需人工）。");
    }

    /// <summary>回执明确失败（nonce 已消耗）：清哈希回 Processing，广播时取新 nonce。</summary>
    private async Task RequeueAfterFailedReceiptAsync(ColWithdrawOrder order, string reason, CancellationToken ct)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.WithdrawOrders
            .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.Broadcasting && o.TxHash == order.TxHash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Processing)
                .SetProperty(o => o.StatusChangedAt, _clock.Now)
                .SetProperty(o => o.TxHash, (string?)null)
                .SetProperty(o => o.NonceUsed, (long?)null)
                .SetProperty(o => o.RetryCount, order.RetryCount + 1)
                .SetProperty(o => o.FailReason, reason), ct);
        if (rows > 0)
        {
            Logger.LogWarning("提现单 {OrderId} 回执失败，回 Processing 重试（第 {Retry} 次）", order.Id, order.RetryCount + 1);
        }
    }

    /// <summary>回执成功 → 完结（CAS + CompleteTime + 账本 payout；幂等：Broadcasting/Completed 双态容许重放）。</summary>
    public virtual async Task CompleteFromReceiptAsync(Guid orderId, string txHash)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var rows = await dbContext.WithdrawOrders
            .Where(o => o.Id == orderId
                        && (o.Status == WithdrawStatus.Broadcasting || o.Status == WithdrawStatus.BroadcastFailed)
                        && o.TxHash == txHash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Completed)
                .SetProperty(o => o.StatusChangedAt, now)
                .SetProperty(o => o.CompleteTime, now)
                .SetProperty(o => o.FailReason, (string?)null));
        if (rows == 0)
        {
            await uow.CompleteAsync();
            return; // 已被并发完结：幂等返回
        }

        var order = await dbContext.WithdrawOrders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        await _ledger.PostPayoutAsync(order, txHash);
        await uow.CompleteAsync();
        Logger.LogInformation("提现单 {OrderId} 打款完成（{TxHash}）", orderId, txHash);
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
        var threshold = await GetDecimalAsync(CollectionSettings.Withdraw.AutoApproveMaxAmount, WithdrawDefaults.AutoApproveMaxAmount);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var pending = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Pending && o.Amount <= threshold)
            .OrderBy(o => o.CreationTime)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var order in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsHaltedAsync())
            {
                break; // 总闸：本轮剩余单都等下一轮
            }

            try
            {
                var wallet = await _guard.LoadAsync(ct);
                if (wallet == null)
                {
                    Logger.LogError("{Prefix}提现热钱包未配置，小额自动审停转", CollectionConsts.AlertPrefixWithdrawHotWallet);
                    break;
                }

                var token = await FindTokenAsync(dbContext, network, order.Currency, ct);
                await _guard.EnsureCanPayAsync(wallet, gateway, token, order.ActualAmount, ct);
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
            .Where(o => o.Status == WithdrawStatus.Processing)
            .OrderBy(o => o.CreationTime)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var order in queue)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsHaltedAsync())
            {
                break;
            }

            try
            {
                await BroadcastOneAsync(network, gateway, order, ct);
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
                // 发送结果未知：保持 Broadcasting 无哈希——卡单恢复会转人工，绝不在此重试
                Logger.LogError(ex, "提现单 {OrderId} 广播异常（发送结果未知），等卡单恢复人工核查", order.Id);
            }
        }
    }

    private async Task BroadcastOneAsync(
        ColNetwork network, IBscChainGateway gateway, ColWithdrawOrder order, CancellationToken ct)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var wallet = await _guard.LoadAsync(ct)
                     ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
        var token = await FindTokenAsync(dbContext, network, order.Currency, ct);

        // 广播前偿付/熔断/BNB 检查（签名临界区内还有最后一道总闸检查）
        await _guard.EnsureCanPayAsync(wallet, gateway, token, order.ActualAmount, ct);

        // 取件 CAS：Processing → Broadcasting + 发送前落库 NonceUsed/BroadcastAt。
        // nonce 必须先于发送持久化：发送结果未知（超时/崩溃）时，「热钱包 pending nonce 是否
        // 已越过 NonceUsed」是唯一可证明「钱没出去/已出去」的判据——先记 nonce 才有无人工的自动仲裁。
        var now = _clock.Now;
        long nonce;
        await SignGate.WaitAsync(ct);
        try
        {
            nonce = await gateway.GetTransactionCountAsync(wallet.Address, ct);
        }
        finally
        {
            SignGate.Release();
        }

        var rows = await dbContext.WithdrawOrders
            .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Broadcasting)
                .SetProperty(o => o.StatusChangedAt, now)
                .SetProperty(o => o.NonceUsed, nonce)
                .SetProperty(o => o.BroadcastAt, now)
                .SetProperty(o => o.FailReason, (string?)null), ct);
        if (rows == 0)
        {
            return;
        }

        // 签名临界区：串行化 + 发送前最后一道总闸（关 TOCTOU，exchange 同款）
        await SignGate.WaitAsync(ct);
        try
        {
            if (await IsHaltedAsync())
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawHalted);
            }

            var masterKey = _configuration["Collection:MasterKey"];
            if (string.IsNullOrEmpty(masterKey))
            {
                throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
            }

            var privateKey = wallet.DecryptKey(masterKey, _encryptor);
            var dataHex = SweepManager.BuildErc20TransferData(order.ToAddress, order.ActualAmount, token.Decimals);
            var txHash = await gateway.SendTransactionAsync(
                privateKey, token.ContractAddress, dataHex, "0x0", network.ChainId, nonce, ct);

            await dbContext.WithdrawOrders
                .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.Broadcasting && o.TxHash == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TxHash, txHash), ct);
            Logger.LogInformation("提现单 {OrderId} 已广播 {Actual} {Currency} → {To}（{TxHash}）",
                order.Id, order.ActualAmount, order.Currency, order.ToAddress, txHash);
        }
        catch (Exception ex)
        {
            // 发送结果未知：保持 Broadcasting 无哈希——nonce 已落库，卡单恢复的 nonce 仲裁
            // 下一轮自动判定（pending ≤ NonceUsed = 可证未出款 → 自动重播/退款），无需人工
            Logger.LogError(ex, "提现单 {OrderId} 广播异常（发送结果未知，NonceUsed={Nonce}），等 nonce 仲裁自动处置", order.Id, nonce);
            throw;
        }
        finally
        {
            SignGate.Release();
        }
    }

    // ── 管理端补救路径（BroadcastFailed 单）─────────────────────────────────────────────

    /// <summary>
    /// 重试（仅 BroadcastFailed 且 RetryCount 未超限）：有哈希先验回执——成功直接完结、
    /// 失败/不匹配清哈希回 Processing；无哈希过双花门（近期已向该地址付款则拒绝）。
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

        var maxRetries = await GetIntAsync(CollectionSettings.Withdraw.MaxRetries, WithdrawDefaults.MaxRetries);
        if (order.RetryCount >= maxRetries)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawRetryLimitReached)
                .WithData("RetryCount", order.RetryCount).WithData("Max", maxRetries);
        }

        if (!string.IsNullOrEmpty(order.TxHash))
        {
            var receipt = await gateway.GetTransactionReceiptAsync(order.TxHash, ct);
            if (receipt.Status == null)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                    .WithData("TxHash", order.TxHash);
            }

            if (receipt.Status == 1)
            {
                await CompleteFromReceiptAsync(order.Id, order.TxHash!);
                return;
            }

            // 回执失败：清哈希回 Processing（新 nonce 重试）
            await dbContext.WithdrawOrders
                .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.BroadcastFailed && o.TxHash == order.TxHash)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, WithdrawStatus.Processing)
                    .SetProperty(o => o.StatusChangedAt, _clock.Now)
                    .SetProperty(o => o.TxHash, (string?)null)
                    .SetProperty(o => o.NonceUsed, (long?)null)
                    .SetProperty(o => o.RetryCount, order.RetryCount + 1)
                    .SetProperty(o => o.FailReason, (string?)"管理端重试（回执失败）"), ct);
            return;
        }

        // 无哈希：双花门——近期（24h）已向该地址付过款即拒绝，人工先核实链上
        var since = _clock.Now.AddDays(-1);
        var recentPayout = await dbContext.WithdrawOrders.AsNoTracking().AnyAsync(
            o => o.ToAddress == order.ToAddress && o.Id != order.Id
                 && o.CompleteTime != null && o.CompleteTime >= since, ct);
        if (recentPayout)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawRecentPayoutExists)
                .WithData("ToAddress", order.ToAddress);
        }

        await dbContext.WithdrawOrders
            .Where(o => o.Id == order.Id && o.Status == WithdrawStatus.BroadcastFailed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Processing)
                .SetProperty(o => o.StatusChangedAt, _clock.Now)
                .SetProperty(o => o.RetryCount, order.RetryCount + 1)
                .SetProperty(o => o.FailReason, (string?)"管理端重试（双花门通过）"), ct);
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    private async Task<ColToken> FindTokenAsync(CollectionDbContext dbContext, ColNetwork network, CollectionCurrency currency, CancellationToken ct)
    {
        var symbol = WithdrawLedgerPoster.CurrencyCode(currency);
        return await dbContext.Tokens.AsNoTracking()
                   .FirstOrDefaultAsync(t => t.NetworkId == network.Id && t.Symbol == symbol, ct)
               ?? throw new Volo.Abp.AbpException($"网络 {network.Name} 缺少代币 {symbol}（种子未建或被禁用）");
    }

    private async Task MarkFailedAsync(Guid orderId, WithdrawStatus from, string reason)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.WithdrawOrders
            .Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.BroadcastFailed)
                .SetProperty(o => o.StatusChangedAt, _clock.Now)
                .SetProperty(o => o.FailReason, reason));
        if (rows > 0)
        {
            Logger.LogError("{Prefix}提现单 {OrderId} → BroadcastFailed：{Reason}",
                CollectionConsts.AlertPrefixWithdrawHotWallet, orderId, reason);
        }
    }

    private async Task<bool> IsHaltedAsync()
    {
        return await _settings.IsTrueAsync(CollectionSettings.Withdraw.Halt);
    }

    private async Task<int> GetIntAsync(string key, int fallback)
    {
        var raw = await _settings.GetOrNullAsync(key);
        return int.TryParse(raw, out var v) && v > 0 ? v : fallback;
    }

    private async Task<decimal> GetDecimalAsync(string key, decimal fallback)
    {
        var raw = await _settings.GetOrNullAsync(key);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : fallback;
    }
}
