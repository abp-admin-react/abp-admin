using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using AbpAdmin.ScheduledJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Jobs;

/// <summary>
/// 订单补状态作业（计划第 3.3 节）：只调用集成服务和自己的表。Host 专用定时作业
///（IScheduledJobHandler + [ExposeServices]，实例字段不跨调用保存状态）。
/// 扫描范围：仍为 PendingPayment 的订单；Paid 且没有任何异常明细、PaidAt 在最近 7 天内的订单；
/// Closed、没有任何异常明细、ExpiresAt 在最近 7 天内的订单。更早的关闭单用管理端列表的预置
/// 过滤查看，不每轮全表扫描，也不另建清单表。
/// M2 起补两步（计划第 3.3 节）：「已扣未标支付」（GetDebitAsync 核对金额币种后补支付/复活/
/// 回冲）与「充值流水补插」（ListCreditedTxnsAsync 序号游标 + 7 天重扫，跳过计数告警）。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class OrderStatusSyncJobHandler : IScheduledJobHandler, ITransientDependency
{
    /// <summary>补状态回看窗口：Paid/Closed 单只看最近 7 天（计划第 3.3 节）。</summary>
    public static readonly TimeSpan LookBackWindow = TimeSpan.FromDays(7);

    private const int BatchSize = 200;

    public string JobType => ScheduledJobConsts.OrderStatusSyncJobType;

    public string DisplayNameKey => "ScheduledJobType:OrderStatusSync";

    private readonly IDbContextProvider<OrderingDbContext> _dbContextProvider;
    private readonly AbpAdmin.Collection.Contracts.ICollectionIntegrationService _integrationService;
    private readonly OrderPaymentManager _orderPaymentManager;
    private readonly IClock _clock;
    private readonly ILogger<OrderStatusSyncJobHandler> _logger;

    public OrderStatusSyncJobHandler(
        IDbContextProvider<OrderingDbContext> dbContextProvider,
        AbpAdmin.Collection.Contracts.ICollectionIntegrationService integrationService,
        OrderPaymentManager orderPaymentManager,
        IClock clock,
        ILogger<OrderStatusSyncJobHandler> logger)
    {
        _dbContextProvider = dbContextProvider;
        _integrationService = integrationService;
        _orderPaymentManager = orderPaymentManager;
        _clock = clock;
        _logger = logger;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        var now = _clock.Now;

        // ── PendingPayment：到账检查 + 过期关单 ─────────────────────────────────────────
        var pendingOrders = await QueryOrdersAsync(o =>
            o.Status == OrderStatus.PendingPayment &&
            o.OrderType == OrderType.Business);

        foreach (var order in pendingOrders)
        {
            try
            {
                if (order.ExpiresAt != null && order.ExpiresAt.Value <= now)
                {
                    await HandleExpiredAsync(order);
                    continue;
                }

                if (order.PayAmount == null)
                {
                    // 尚未发起支付：补状态作业不做金额比较（计划第 3.1 节）
                    continue;
                }

                var completed = await CompletedCollectionsAsync(order);
                if (completed.Count == 0)
                {
                    continue;
                }

                var match = FindMatching(completed, order);
                if (match != null)
                {
                    await _orderPaymentManager.HandleCollectionCompletedAsync(
                        order.Id, match.Id,
                        OrderingPayChannelExtensions.FromCollectionChannel(match.Channel),
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(match.Amount),
                        match.Currency, match.ChannelTradeNo);
                }
                else if (!order.PaymentAnomaly)
                {
                    // 有一笔 Completed 但金额或币种不一致：只追加一次异常明细（PaymentAnomaly
                    ///标志位在首次追加时置位，本轮询不重复写行），不关单，关单留到订单自己的
                    ///ExpiresAt（无去重时 5 分钟一轮会刷出上百重复行）
                    var mismatch = completed[0];
                    await _orderPaymentManager.AppendAnomalyAsync(
                        order.Id,
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(mismatch.Amount),
                        mismatch.Currency, mismatch.Id, mismatch.ChannelTradeNo,
                        "Reconciliation found a completed collection with mismatched amount or currency.");
                }
            }
            catch (Exception ex)
            {
                // 单个订单失败不挡住整轮（对齐集成服务的单行跳过纪律）
                _logger.LogWarning(ex, "补状态处理订单 {OrderId} 失败，下一轮重试", order.Id);
            }
        }

        // ── Paid（7 天内、无异常明细）：第二笔到账兜底 ───────────────────────────────────
        var paidOrders = await QueryOrdersAsync(o =>
            o.Status == OrderStatus.Paid &&
            !o.PaymentAnomaly &&
            o.PaidAt != null && o.PaidAt > now - LookBackWindow &&
            o.CollectionId != null);

        foreach (var order in paidOrders)
        {
            try
            {
                var completed = await CompletedCollectionsAsync(order);
                if (completed.Count == 0)
                {
                    continue;
                }

                // 订单已是 Paid，业务单下另有一笔 Completed 且不是致付来源：无论金额是否一致，
                // 都追加异常明细并告警（计划第 3.3 节）。余额支付的单（BalanceTxnId 非空）残留的
                // CollectionId 是换渠道前的旧归因，任何完成的收款都算第二笔；
                // 渠道支付的单只有收款号不等于致付归因才算
                var second = completed.FirstOrDefault(c =>
                    order.BalanceTxnId != null || c.Id != order.CollectionId);
                if (second != null)
                {
                    await _orderPaymentManager.AppendAnomalyAsync(
                        order.Id,
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(second.Amount),
                        second.Currency, second.Id, second.ChannelTradeNo,
                        "Reconciliation found another completed collection after the order was paid.");
                    _logger.LogWarning("订单 {OrderId}（已支付）发现第二笔完成收款 {CollectionId}",
                        order.Id, second.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "补状态检查已支付订单 {OrderId} 失败，下一轮重试", order.Id);
            }
        }

        // ── Closed（7 天内、无异常明细）：匹配到账的复活 / 告警 ──────────────────────────
        var closedOrders = await QueryOrdersAsync(o =>
            o.Status == OrderStatus.Closed &&
            !o.PaymentAnomaly &&
            o.ExpiresAt != null && o.ExpiresAt > now - LookBackWindow);

        foreach (var order in closedOrders)
        {
            try
            {
                if (order.PayAmount == null)
                {
                    continue;
                }

                var completed = await CompletedCollectionsAsync(order);
                if (completed.Count == 0)
                {
                    continue;
                }

                var match = FindMatching(completed, order);
                if (match != null)
                {
                    // CloseReason 决定复活或只告警：Expired 可标回 Paid；UserCancel 只告警
                    await _orderPaymentManager.HandleCollectionCompletedAsync(
                        order.Id, match.Id,
                        OrderingPayChannelExtensions.FromCollectionChannel(match.Channel),
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(match.Amount),
                        match.Currency, match.ChannelTradeNo);
                }
                else
                {
                    // 金额或币种不一致的 Completed：追加异常明细，不复活
                    var mismatch = completed[0];
                    await _orderPaymentManager.AppendAnomalyAsync(
                        order.Id,
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(mismatch.Amount),
                        mismatch.Currency, mismatch.Id, mismatch.ChannelTradeNo,
                        "Reconciliation found a mismatched completed collection on a closed order.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "补状态检查已关闭订单 {OrderId} 失败，下一轮重试", order.Id);
            }
        }

        // ── M2：已扣未标支付（计划第 3.3 节）────────────────────────────────────────────
        await ReconcileUnmarkedDebitsAsync(now);

        // ── M2：充值流水补插（计划第 3.3 节：序号游标 + 7 天重扫 + 跳过计数）────────────
        await BackfillCreditedTxnsAsync(context);
    }

    /// <summary>
    /// 「已扣未标支付」：扣款和标支付本应同事务原子提交，这一步是为跨模块旁路留下的兜底——
    /// PendingPayment 且有金额币种一致的未回冲扣款 → 补标 Paid（写 BalanceTxnId、PaidAt）；
    /// Closed(Expired)、无异常明细、金额一致 → 标回 Paid 并写归因；
    /// Closed(UserCancel) 且扣款未回冲 → 回冲扣款并追加异常明细（不复活，计划第 3.3 节）。
    /// </summary>
    private async Task ReconcileUnmarkedDebitsAsync(DateTime now)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var pendingOrders = await QueryOrdersAsync(o =>
            o.Status == OrderStatus.PendingPayment && o.OrderType == OrderType.Business);

        foreach (var order in pendingOrders)
        {
            try
            {
                var debit = await _integrationService.GetDebitAsync(OrderingConsts.OrderBusinessType, order.Id);
                if (debit == null)
                {
                    continue;
                }

                // 金额币种与订单应付一致才补支付；对不上只告警（错额扣款留给人工）
                if (order.PayAmount == null
                    || order.PayCurrency != debit.Currency
                    || order.PayAmount.Value != AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(debit.Amount))
                {
                    _logger.LogWarning(
                        "订单 {OrderId} 有未回冲扣款但金额币种与应付不一致（应付 {PayAmount} {PayCurrency}，扣款 {Amount} {Currency}），留给人工处理",
                        order.Id, order.PayAmount, order.PayCurrency, debit.Amount, debit.Currency);
                    continue;
                }

                // 状态转移统一走状态机（作业只编排扫描，不自己拼 Paid 的列集）
                if (await _orderPaymentManager.MarkPaidFromDebitAsync(order.Id, debit.BalanceTxnId, debit.Currency))
                {
                    _logger.LogWarning("订单 {OrderId} 由补状态作业按未回冲扣款补标 Paid（BalanceTxnId={TxnId}）",
                        order.Id, debit.BalanceTxnId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "已扣未标支付核对订单 {OrderId} 失败，下一轮重试", order.Id);
            }
        }

        var closedOrders = await QueryOrdersAsync(o =>
            o.Status == OrderStatus.Closed &&
            !o.PaymentAnomaly &&
            o.OrderType == OrderType.Business &&
            o.ExpiresAt != null && o.ExpiresAt > now - LookBackWindow);

        foreach (var order in closedOrders)
        {
            try
            {
                var debit = await _integrationService.GetDebitAsync(OrderingConsts.OrderBusinessType, order.Id);
                if (debit == null)
                {
                    continue;
                }

                var amount = AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(debit.Amount);
                var amountsMatch = order.PayAmount != null
                    && order.PayCurrency == debit.Currency
                    && order.PayAmount.Value == amount;

                if (order.CloseReason == OrderCloseReason.Expired)
                {
                    if (amountsMatch)
                    {
                        // 过期关闭 + 金额一致 + 无异常明细：标回 Paid 并写归因（余额版复活，状态机统一守卫）
                        if (await _orderPaymentManager.ReviveFromDebitAsync(order.Id, amount, debit.Currency, debit.BalanceTxnId))
                        {
                            _logger.LogWarning("过期关闭的订单 {OrderId} 按未回冲扣款复活为 Paid（BalanceTxnId={TxnId}）",
                                order.Id, debit.BalanceTxnId);
                        }

                        continue;
                    }

                    // 过期关闭 + 扣款金额币种对不上：不能复活也不能静默跳过——扣着的钱必须有去处，
                    // 追加异常明细进人工队列（否则既不告警也不留痕，出了 7 天窗口就永久失联）
                    await _orderPaymentManager.AppendAnomalyAsync(order.Id, amount, debit.Currency,
                        null, debit.BalanceTxnId.ToString(),
                        "Expired order has an unreversed debit with mismatched amount or currency; needs manual resolution.");
                    _logger.LogWarning(
                        "过期关闭的订单 {OrderId} 有未回冲扣款但金额币种不一致（应付 {PayAmount} {PayCurrency}，扣款 {Amount} {Currency}），已进异常队列",
                        order.Id, order.PayAmount, order.PayCurrency, debit.Amount, debit.Currency);
                    continue;
                }

                if (order.CloseReason == OrderCloseReason.UserCancel)
                {
                    // 用户取消后的扣款：回冲 + 追加异常明细（不复活，计划第 3.3 节）
                    await _integrationService.ReverseDebitAsync(OrderingConsts.OrderBusinessType, order.Id);
                    await _orderPaymentManager.AppendAnomalyAsync(order.Id, amount, debit.Currency,
                        null, debit.BalanceTxnId.ToString(),
                        "Order was cancelled by the user, but an unreversed balance debit was found; debit reversed.");
                    _logger.LogWarning("用户取消的订单 {OrderId} 发现未回冲扣款，已回冲（BalanceTxnId={TxnId}）",
                        order.Id, debit.BalanceTxnId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "已扣未标支付核对关闭订单 {OrderId} 失败，下一轮重试", order.Id);
            }
        }
    }

    /// <summary>
    /// 充值流水补插：序号游标正向拉一批，没有对应充值订单的按 BalanceTxnId 补插（停在 Paid）。
    /// 单行失败跳过并告警，游标推进到本批最大序号（含跳过的行，被跳过的行只靠 7 天重扫补）；
    /// 同一 BalanceTxnId 连续跳过超过阈值告警并重新计数；游标 7 天没前进告警（计划第 3.1 节）。
    /// </summary>
    private async Task BackfillCreditedTxnsAsync(ScheduledJobContext context)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var state = await dbContext.JobStates.FindAsync(ScheduledJobConsts.OrderStatusSyncJobType);
        if (state == null)
        {
            state = new OrdJobState(ScheduledJobConsts.OrderStatusSyncJobType);
            await dbContext.JobStates.AddAsync(state);
        }

        var threshold = ParseSkipAlertThreshold(context.Payload);
        var skipped = DeserializeSkipped(state.SkippedTxnsJson);

        var result = await _integrationService.ListCreditedTxnsAsync(state.CreditCursor, BatchSize);

        // 批量去重：7 天重扫每轮都会带回已处理行，逐笔 AnyAsync 是每轮 O(批大小) 的点查；
        // 一次 IN 查出已存在的充值订单，剩下的才走插入
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
            if (existingIds.Contains(txn.BalanceTxnId))
            {
                skipped.Remove(txn.BalanceTxnId);
                continue;
            }

            try
            {
                await _orderPaymentManager.HandleBalanceCreditedAsync(
                    txn.BalanceTxnId, txn.UserId, txn.Currency,
                    AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(txn.Amount), txn.CreditedAt);
                skipped.Remove(txn.BalanceTxnId);
            }
            catch (Exception ex)
            {
                // 单行失败跳过并告警；连续跳过计数 +1，超过阈值再告警并重新计数
                var count = (skipped.TryGetValue(txn.BalanceTxnId, out var c) ? c : 0) + 1;
                _logger.LogWarning(ex, "充值流水补插失败，跳过（BalanceTxnId={TxnId}，连续第 {Count} 轮）",
                    txn.BalanceTxnId, count);
                if (count >= threshold)
                {
                    _logger.LogWarning("充值流水 {TxnId} 连续跳过超过 {Threshold} 轮，请人工检查", txn.BalanceTxnId, threshold);
                    skipped[txn.BalanceTxnId] = 0;
                }
                else
                {
                    skipped[txn.BalanceTxnId] = count;
                }
            }
        }

        // 游标前进检测：超过 7 天没前进告警（计划第 3.1 节；序号与时间双轨防漏的另一半）
        if (result.Cursor > state.CreditCursor)
        {
            state.CreditCursor = result.Cursor;
            state.CursorLastAdvancedAt = now;
        }
        else if (state.CursorLastAdvancedAt != null && now - state.CursorLastAdvancedAt > LookBackWindow)
        {
            _logger.LogWarning("充值序号游标超过 7 天没有前进（cursor={Cursor}）——入账停滞或丢行，请检查", state.CreditCursor);
            state.CursorLastAdvancedAt = now; // 告警节流：再等一个窗口
        }

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
    /// 已过订单自己的 ExpiresAt 且仍是 PendingPayment：先 CancelAsync；
    /// 返回已完成且金额币种一致则标 Paid 并写归因；不一致则追加异常明细并 Closed(Expired)；
    /// 已取消/已关闭则把订单改为 Closed(Expired)（计划第 3.3 节）。
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

                if (amountsMatch)
                {
                    await _orderPaymentManager.HandleCollectionCompletedAsync(
                        order.Id, cancel.Collection.Id,
                        OrderingPayChannelExtensions.FromCollectionChannel(cancel.Collection.Channel),
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(cancel.Collection.Amount),
                        cancel.Collection.Currency, cancel.Collection.ChannelTradeNo);
                }
                else
                {
                    await _orderPaymentManager.AppendAnomalyAsync(
                        order.Id,
                        AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(cancel.Collection.Amount),
                        cancel.Collection.Currency, cancel.Collection.Id, cancel.Collection.ChannelTradeNo,
                        "Expired order found a completed collection with mismatched amount or currency.");
                    await _orderPaymentManager.CloseExpiredAsync(order);
                }

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

    private async Task<List<OrdOrder>> QueryOrdersAsync(Expression<Func<OrdOrder, bool>> predicate)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.OrderType == OrderType.Business)
            .Where(predicate)
            .OrderBy(o => o.CreationTime)
            .Take(BatchSize)
            .ToListAsync();
    }
}
