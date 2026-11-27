using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using AbpAdmin.ScheduledJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Jobs;

/// <summary>
/// 宿主定时核对待支付和近期已支付、已关闭的订单，补偿已确认的收款、未回冲的扣款和遗漏的充值订单。
/// 收款与余额通过集成服务读取，订单状态交给订单支付管理器更新。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class OrderStatusSyncJobHandler : IScheduledJobHandler, ITransientDependency
{
    /// <summary>已支付和已关闭订单的 7 天回看窗口。</summary>
    public static readonly TimeSpan LookBackWindow = TimeSpan.FromDays(7);

    private const int BatchSize = 200;

    public string JobType => ScheduledJobConsts.OrderStatusSyncJobType;

    public string DisplayNameKey => "ScheduledJobType:OrderStatusSync";

    private readonly IDbContextProvider<OrderingDbContext> _dbContextProvider;
    private readonly AbpAdmin.Collection.Contracts.ICollectionIntegrationService _integrationService;
    private readonly OrderPaymentManager _orderPaymentManager;
    private readonly IUnitOfWorkManager _units;
    private readonly IClock _clock;
    private readonly ILogger<OrderStatusSyncJobHandler> _logger;

    public OrderStatusSyncJobHandler(
        IDbContextProvider<OrderingDbContext> dbContextProvider,
        AbpAdmin.Collection.Contracts.ICollectionIntegrationService integrationService,
        OrderPaymentManager orderPaymentManager,
        IClock clock,
        IUnitOfWorkManager units,
        ILogger<OrderStatusSyncJobHandler> logger)
    {
        _dbContextProvider = dbContextProvider;
        _integrationService = integrationService;
        _orderPaymentManager = orderPaymentManager;
        _clock = clock;
        _units = units;
        _logger = logger;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        var now = _clock.Now;
        var db = await _dbContextProvider.GetDbContextAsync();
        var state = await db.JobStates.FindAsync(ScheduledJobConsts.OrderStatusSyncJobType);
        if (state == null)
        {
            state = new OrdJobState(ScheduledJobConsts.OrderStatusSyncJobType) { UpdatedAt = now };
            db.JobStates.Add(state);
        }

        // ── PendingPayment：到账检查 + 过期关单 ─────────────────────────────────────────
        state.PendingCollectionBeforeId = await ProcessOrdersAsync(state.PendingCollectionBeforeId, o =>
            o.Status == OrderStatus.PendingPayment, async order =>
        {
            if (order.ExpiresAt != null && order.ExpiresAt.Value <= now)
            {
                await HandleExpiredAsync(order);
                return;
            }

            if (order.PayAmount == null)
            {
                return;
            }

            var completed = await CompletedCollectionsAsync(order);
            if (completed.Count == 0)
            {
                return;
            }

            await _orderPaymentManager.HandleCompletedCollectionAsync(order.Id, FindMatching(completed, order) ?? completed[0]);
        }, context.CancellationToken);

        // ── Paid（7 天内、无异常标志位、已归因 CollectionId）：第二笔到账兜底 ────────────────
        state.PaidCollectionBeforeId = await ProcessOrdersAsync(state.PaidCollectionBeforeId, o =>
            o.Status == OrderStatus.Paid &&
            !o.PaymentAnomaly &&
            o.PaidAt != null && o.PaidAt > now - LookBackWindow &&
            o.CollectionId != null, async order =>
        {
            var completed = await CompletedCollectionsAsync(order);
            if (completed.Count == 0)
            {
                return;
            }

            // 余额支付已完成时，旧渠道留下的 CollectionId 不能证明这笔渠道收款就是付款来源。
            var second = completed.FirstOrDefault(c =>
                order.BalanceTxnId != null || c.Id != order.CollectionId);
            if (second != null)
            {
                await _orderPaymentManager.HandleCompletedCollectionAsync(order.Id, second);
            }
        }, context.CancellationToken);

        // ── Closed（7 天内、无异常标志位）：匹配到账的复活 / 告警 ──────────────────────────
        state.ClosedCollectionBeforeId = await ProcessOrdersAsync(state.ClosedCollectionBeforeId, o =>
            o.Status == OrderStatus.Closed &&
            !o.PaymentAnomaly &&
            o.ExpiresAt != null && o.ExpiresAt > now - LookBackWindow, async order =>
        {
            if (order.PayAmount == null)
            {
                return;
            }

            var completed = await CompletedCollectionsAsync(order);
            if (completed.Count == 0)
            {
                return;
            }

            await _orderPaymentManager.HandleCompletedCollectionAsync(order.Id, FindMatching(completed, order) ?? completed[0]);
        }, context.CancellationToken);

        await ReconcileUnmarkedDebitsAsync(state, now, context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
        await BackfillCreditedTxnsAsync(context, state);
    }

    /// <summary>
    /// 修复未回冲的余额扣款。待支付或过期关闭订单只在付款人、金额和币种一致且未退款时补标已支付。
    /// 用户取消的订单走原扣款回冲，遇到退款或缺失凭证时记录异常供人工处理。
    /// </summary>
    private async Task ReconcileUnmarkedDebitsAsync(OrdJobState state, DateTime now, CancellationToken cancellationToken)
    {
        state.PendingDebitBeforeId = await ProcessOrdersAsync(state.PendingDebitBeforeId, o =>
            o.Status == OrderStatus.PendingPayment, async order =>
        {
            var debit = await _integrationService.GetDebitAsync(OrderingConsts.OrderBusinessType, order.Id);
            if (debit == null)
            {
                return;
            }

            if (!CollectionAmounts.TryParsePositiveExact(debit.Amount, debit.Currency, out var amount)
                || order.PayAmount == null
                || order.UserId != debit.UserId
                || order.PayCurrency != debit.Currency
                || order.PayAmount.Value != amount)
            {
                _logger.LogWarning(
                    "订单 {OrderId} 的未回冲扣款 {TxnId} 无法匹配付款人、应付金额或币种（应付 {PayAmount} {PayCurrency}，扣款 {Amount} {Currency}），待人工核对",
                    order.Id, debit.BalanceTxnId, order.PayAmount, order.PayCurrency, debit.Amount, debit.Currency);
                return;
            }

            if (await _integrationService.HasWalletRefundAsync(debit.BalanceTxnId))
            {
                _logger.LogWarning(
                    "订单 {OrderId} 的扣款 {TxnId} 已有钱包退款记录，暂不补标已支付",
                    order.Id, debit.BalanceTxnId);
                return;
            }

            if (await _orderPaymentManager.MarkPaidFromDebitAsync(order.Id, debit.UserId, amount, debit.Currency, debit.BalanceTxnId))
            {
                _logger.LogWarning("订单 {OrderId} 由补状态作业按未回冲扣款补标 Paid（BalanceTxnId={TxnId}）",
                    order.Id, debit.BalanceTxnId);
            }
        }, cancellationToken);

        state.ClosedDebitBeforeId = await ProcessOrdersAsync(state.ClosedDebitBeforeId, o =>
            o.Status == OrderStatus.Closed &&
            !o.PaymentAnomaly &&
            o.ExpiresAt != null && o.ExpiresAt > now - LookBackWindow, async order =>
        {
            var debit = await _integrationService.GetDebitAsync(OrderingConsts.OrderBusinessType, order.Id);
            if (debit == null)
            {
                return;
            }

            var validAmount = CollectionAmounts.TryParsePositiveExact(debit.Amount, debit.Currency, out var amount);
            if (!validAmount || order.UserId != debit.UserId || debit.Currency is not (CollectionCurrency.Usdt or CollectionCurrency.Usdc))
            {
                await _orderPaymentManager.AppendAnomalyAsync(order.Id, validAmount ? amount : null, debit.Currency, null,
                    debit.BalanceTxnId.ToString(), "Invalid balance debit owner, amount or currency; needs manual resolution.");
                return;
            }

            var amountsMatch = order.PayAmount != null
                && order.PayCurrency == debit.Currency
                && order.PayAmount.Value == amount;

            if (order.CloseReason == OrderCloseReason.Expired)
            {
                if (amountsMatch && await _integrationService.HasWalletRefundAsync(debit.BalanceTxnId))
                {
                    _logger.LogWarning(
                        "过期关闭的订单 {OrderId} 的扣款 {TxnId} 已有钱包退款记录，暂不恢复为已支付",
                        order.Id, debit.BalanceTxnId);
                    return;
                }

                if (amountsMatch)
                {
                    if (await _orderPaymentManager.ReviveFromDebitAsync(order.Id, debit.UserId, amount, debit.Currency, debit.BalanceTxnId))
                    {
                        _logger.LogWarning("过期关闭的订单 {OrderId} 按未回冲扣款复活为 Paid（BalanceTxnId={TxnId}）",
                            order.Id, debit.BalanceTxnId);
                    }

                    return;
                }

                // 金额或币种不符时保留异常记录，供订单移出自动核对窗口后继续处理。
                await _orderPaymentManager.AppendAnomalyAsync(order.Id, amount, debit.Currency,
                    null, debit.BalanceTxnId.ToString(),
                    "Expired order has an unreversed debit with mismatched amount or currency; needs manual resolution.");
                _logger.LogWarning(
                    "过期关闭的订单 {OrderId} 有未回冲扣款但金额币种不一致（应付 {PayAmount} {PayCurrency}，扣款 {Amount} {Currency}），已进异常队列",
                    order.Id, order.PayAmount, order.PayCurrency, debit.Amount, debit.Currency);
                return;
            }

            if (order.CloseReason == OrderCloseReason.UserCancel)
            {
                try
                {
                    await _integrationService.ReverseDebitAsync(OrderingConsts.OrderBusinessType, order.Id);
                    await _orderPaymentManager.AppendAnomalyAsync(order.Id, amount, debit.Currency,
                        null, debit.BalanceTxnId.ToString(),
                        "Order was cancelled by the user, but an unreversed balance debit was found; debit reversed.");
                    _logger.LogWarning("用户取消的订单 {OrderId} 发现未回冲扣款，已回冲（BalanceTxnId={TxnId}）",
                        order.Id, debit.BalanceTxnId);
                }
                catch (BusinessException ex) when (ex.Code == AbpAdmin.Collection.Contracts.CollectionErrorCodes.WalletLedgerPending
                    || ex.Code == AbpAdmin.Collection.Contracts.CollectionErrorCodes.WalletRefundNotAllowed)
                {
                    var blockedByRefund = ex.Code == AbpAdmin.Collection.Contracts.CollectionErrorCodes.WalletRefundNotAllowed;
                    await _orderPaymentManager.AppendAnomalyAsync(order.Id, amount, debit.Currency,
                        null, debit.BalanceTxnId.ToString(),
                        blockedByRefund
                            ? "Unreversed debit already has a wallet refund. Compensation will not reverse it."
                            : "Unreversed debit has no finance voucher. Left visible for confirmation; compensation will not invent a reversing entry.");
                    _logger.LogWarning(
                        blockedByRefund
                            ? "订单 {OrderId} 的扣款 {TxnId} 已有钱包退款，已停止自动回冲"
                            : "订单 {OrderId} 的扣款 {TxnId} 没有财务凭证，已停止自动回冲",
                        order.Id, debit.BalanceTxnId);
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 正向游标处理新增流水，反向游标分批重扫最近七天。按 BalanceTxnId 补建已支付的充值订单。
    /// 单笔失败由重扫重试，连续失败达到阈值时告警并重新计数。
    /// </summary>
    private async Task BackfillCreditedTxnsAsync(ScheduledJobContext context, OrdJobState state)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var threshold = ParseSkipAlertThreshold(context.Payload);
        var skipped = DeserializeSkipped(state.SkippedTxnsJson);

        var rescanStartedAt = state.CreditRescanStartedAt ?? now;
        var result = await _integrationService.ListCreditedTxnsAsync(
            state.CreditCursor, BatchSize, state.CreditRescanBeforeSeq, rescanStartedAt);

        // 重扫包含已处理的流水，先批量查出已有的充值订单。
        var candidateIds = result.Items.Select(t => t.BalanceTxnId).ToList();
        var existingIds = candidateIds.Count == 0
            ? new HashSet<Guid>()
            : (await dbContext.Orders
                    .Where(o => o.OrderType == OrderType.TopUp && candidateIds.Contains(o.BalanceTxnId!.Value))
                    .Select(o => o.BalanceTxnId!.Value)
                    .ToListAsync())
                .ToHashSet();

        foreach (var txn in result.Items)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (existingIds.Contains(txn.BalanceTxnId))
            {
                skipped.Remove(txn.BalanceTxnId);
                continue;
            }

            try
            {
                using var credit = _units.Begin(requiresNew: true, isTransactional: true);
                await _orderPaymentManager.HandleBalanceCreditedAsync(
                    txn.BalanceTxnId, txn.UserId, txn.Currency, txn.Amount, txn.CreditedAt);
                await credit.CompleteAsync(context.CancellationToken);
                skipped.Remove(txn.BalanceTxnId);
            }
            catch (Exception ex) when (!context.CancellationToken.IsCancellationRequested)
            {
                var count = (skipped.TryGetValue(txn.BalanceTxnId, out var c) ? c : 0) + 1;
                _logger.LogWarning(ex, "充值流水补插失败并跳过（BalanceTxnId={TxnId}，累计未成功 {Count} 次）",
                    txn.BalanceTxnId, count);
                if (count >= threshold)
                {
                    _logger.LogWarning("充值流水 {TxnId} 补插失败次数达到 {Threshold}，请人工检查", txn.BalanceTxnId, threshold);
                    skipped[txn.BalanceTxnId] = 0;
                }
                else
                {
                    skipped[txn.BalanceTxnId] = count;
                }
            }
        }

        if (result.Cursor > state.CreditCursor)
        {
            state.CreditCursor = result.Cursor;
            state.CursorLastAdvancedAt = now;
        }
        else if (state.CursorLastAdvancedAt != null && now - state.CursorLastAdvancedAt > LookBackWindow)
        {
            _logger.LogWarning("充值补插游标超过 7 天未前进，检查是否有未处理的充值流水（Cursor={Cursor}）", state.CreditCursor);
            state.CursorLastAdvancedAt = now; // 告警节流：再等一个窗口
        }

        state.CreditRescanBeforeSeq = result.RescanBeforeSeq;
        state.CreditRescanStartedAt = result.RescanBeforeSeq == 0 ? null : rescanStartedAt;
        state.SkippedTxnsJson = skipped.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(skipped)
            : null;
        state.UpdatedAt = now;
        await dbContext.SaveChangesAsync();
    }

    private static int ParseSkipAlertThreshold(string? payload)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payload))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                // 根不是对象（"5"、"[1]" 是合法 JSON）时 TryGetProperty 抛 InvalidOperationException——
                // 不在 JsonException 之列，会把整轮补插卡死。先验 ValueKind，坏配置一律退默认值
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("SkipAlertThreshold", out var v)
                    && v.ValueKind == System.Text.Json.JsonValueKind.Number
                    && v.TryGetInt32(out var t) && t > 0)
                {
                    return t;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // 配置非法用默认值，作业不因配置坏而停
        }

        return 20;
    }

    private static Dictionary<Guid, int> DeserializeSkipped(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<Guid, int>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, int>>(json) ?? new();
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<Guid, int>();
        }
    }

    /// <summary>
    /// 过期订单先关渠道单。明确已支付时按到账规则处理，结果仍待确认时保留待支付状态供下轮核对。
    /// </summary>
    private async Task HandleExpiredAsync(OrdOrder order)
    {
        if (order.CollectionId != null)
        {
            var cancel = await _integrationService.CancelAsync(order.CollectionId.Value);
            if (cancel.Collection.Status == AbpAdmin.Collection.Contracts.CollectionStatus.Completed)
            {
                var amountsMatch = order.PayAmount != null
                    && cancel.Collection.Currency == order.PayCurrency
                    && AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(cancel.Collection.Amount) == order.PayAmount.Value;

                await _orderPaymentManager.HandleCompletedCollectionAsync(order.Id, cancel.Collection);
                if (!amountsMatch)
                {
                    await _orderPaymentManager.CloseExpiredAsync(order);
                }

                return;
            }

            if (cancel.Collection.Status == AbpAdmin.Collection.Contracts.CollectionStatus.Pending)
            {
                // 等待渠道查单或关单给出明确结果。下一轮继续处理原收款。
                return;
            }
        }

        await _orderPaymentManager.CloseExpiredAsync(order);
    }

    /// <summary>该业务单下已完成的收款列表（补状态三段扫描共用）。</summary>
    private async Task<List<AbpAdmin.Collection.Contracts.CollectionDto>> CompletedCollectionsAsync(OrdOrder order)
    {
        var list = await _integrationService.ListByBusinessAsync(OrderingConsts.OrderBusinessType, order.Id);
        return list.Where(c => c.Status == AbpAdmin.Collection.Contracts.CollectionStatus.Completed).ToList();
    }

    /// <summary>金额币种与订单应付一致的完成收款；null = 无匹配（错额）。</summary>
    private static AbpAdmin.Collection.Contracts.CollectionDto? FindMatching(
        List<AbpAdmin.Collection.Contracts.CollectionDto> completed, OrdOrder order)
    {
        return completed.FirstOrDefault(c =>
            c.Currency == order.PayCurrency &&
            AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(c.Amount) == order.PayAmount!.Value);
    }

    private async Task<Guid?> ProcessOrdersAsync(Guid? before, Expression<Func<OrdOrder, bool>> predicate,
        Func<OrdOrder, Task> process, CancellationToken cancellationToken)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var candidates = db.Orders.AsNoTracking().Where(o => o.OrderType == OrderType.Business).Where(predicate);
        if (before.HasValue)
        {
            candidates = candidates.Where(o => o.Id < before.Value);
        }

        var page = await candidates.OrderByDescending(o => o.Id).Take(BatchSize).ToListAsync(cancellationToken);
        foreach (var order in page)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 查单可能访问外部渠道。逐单隔离工作单元，写操作由状态机开启事务。
                using var item = _units.Begin(requiresNew: true, isTransactional: false);
                await process(order);
                await item.CompleteAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "补状态处理订单 {OrderId} 失败，订单仍满足核对条件时会随下一轮扫描重试", order.Id);
            }
        }

        if (page.Count < BatchSize)
        {
            return null;
        }
        var last = page[^1].Id;
        return await candidates.AnyAsync(o => o.Id < last, cancellationToken) ? last : null;
    }
}
