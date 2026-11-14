using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Orders;

/// <summary>发起支付的结果。</summary>
public enum StartPayOutcome
{
    /// <summary>发起了新的渠道收款（返回二维码/跳转凭据）。</summary>
    ChannelCreated = 0,

    /// <summary>余额扣款与标支付在同一工作单元提交成功。</summary>
    BalancePaid = 1,

    /// <summary>取消旧收款时发现渠道已完成，订单已按到账规则处理（不再创建新收款、不扣款）。</summary>
    AlreadyCompleted = 2,

    /// <summary>订单已关闭。</summary>
    Closed = 3,
}

public class StartPayResult
{
    public StartPayOutcome Outcome { get; init; }

    public AbpAdmin.Collection.Contracts.CollectionDto? Collection { get; init; }

    public OrdOrder Order { get; init; } = default!;
}

/// <summary>
/// 订单支付状态机。余额扣款与待支付订单转为已支付共用一个事务；并发更新失败时一起回滚。
/// 渠道调用在扣款事务之外执行。
/// </summary>
public class OrderPaymentManager : DomainService
{
    private const string CollectionMismatchNote = "Completed collection amount or currency mismatch.";
    private const string DuplicateCollectionNote = "A second completed collection arrived after the order was paid.";

    private readonly IDbContextProvider<OrderingDbContext> _dbContextProvider;
    private readonly AbpAdmin.Collection.Contracts.ICollectionIntegrationService _integrationService;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly Volo.Abp.EventBus.Distributed.IDistributedEventBus _distributedEventBus;

    public OrderPaymentManager(
        IDbContextProvider<OrderingDbContext> dbContextProvider,
        AbpAdmin.Collection.Contracts.ICollectionIntegrationService integrationService,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        Volo.Abp.EventBus.Distributed.IDistributedEventBus distributedEventBus)
    {
        _dbContextProvider = dbContextProvider;
        _integrationService = integrationService;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _distributedEventBus = distributedEventBus;
    }

    // ── 发起支付（外部渠道，计划第 6 节时序）──────────────────────────────────────────────

    /// <summary>
    /// 换渠道或首次发起。前置：方法必须是渠道方式；报价仍有效。
    /// 改用不同报价前先关当前收款，原报价重试沿用同一收款尝试。
    /// 收款与报价绑定后，渠道请求才会发出。
    /// </summary>
    public virtual async Task<StartPayResult> StartChannelPayAsync(OrdOrder order, OrdQuote quote)
    {
        AssertPayable(order, quote);

        if (order.QuoteId != quote.Id && await CancelPendingCollectionAsync(order))
        {
            return new StartPayResult
            {
                Outcome = StartPayOutcome.AlreadyCompleted,
                Order = (await ReloadAsync(order.Id))!,
            };
        }

        CollectionDto created;
        try
        {
            created = await _integrationService.CreateAsync(new CreateCollectionInput
            {
                BusinessType = OrderingConsts.OrderBusinessType,
                BusinessId = order.Id,
                UserId = order.UserId,
                Channel = quote.PayMethod.ToCollectionChannel()!.Value,
                Currency = quote.PayCurrency,
                Amount = CollectionAmounts.Format(quote.PayAmount),
                ExpiresAt = quote.ExpiresAt,
            }, prepared => BindPreparedCollectionAsync(order, quote, prepared));
        }
        catch (BusinessException ex) when (ex.Code == OrderingErrorCodes.InvalidOrderStatus)
        {
            var current = (await ReloadAsync(order.Id))!;
            if (current.Status != OrderStatus.PendingPayment)
            {
                return new StartPayResult
                {
                    Outcome = current.Status == OrderStatus.Closed ? StartPayOutcome.Closed : StartPayOutcome.AlreadyCompleted,
                    Order = current,
                };
            }

            throw;
        }

        if (created.Status == CollectionStatus.Completed)
        {
            await HandleCompletedCollectionAsync(order.Id, created);
        }

        var createdOrder = await ReloadAsync(order.Id)
            ?? throw new InvalidOperationException($"Order {order.Id} vanished right after channel creation.");
        return new StartPayResult
        {
            Outcome = createdOrder.Status switch
            {
                OrderStatus.Paid or OrderStatus.Fulfilled => StartPayOutcome.AlreadyCompleted,
                OrderStatus.Closed => StartPayOutcome.Closed,
                _ when created.Status == CollectionStatus.Completed => StartPayOutcome.AlreadyCompleted,
                _ => StartPayOutcome.ChannelCreated,
            },
            Collection = createdOrder.Status == OrderStatus.PendingPayment && created.Status == CollectionStatus.Pending
                ? created
                : null,
            Order = createdOrder,
        };
    }

    /// <summary>和本地收款单共用事务，外部渠道可回调前已关联订单与报价。</summary>
    private async Task BindPreparedCollectionAsync(OrdOrder order, OrdQuote quote, CollectionDto prepared)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment
                && (o.CollectionId == order.CollectionId || o.CollectionId == prepared.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PayAmount, quote.PayAmount)
                .SetProperty(o => o.PayCurrency, quote.PayCurrency)
                .SetProperty(o => o.PayChannel, quote.PayMethod)
                .SetProperty(o => o.QuoteId, quote.Id)
                .SetProperty(o => o.CollectionId, prepared.Id));

        if (rows == 0)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus).WithData("OrderId", order.Id);
        }
    }

    // ── 余额支付（计划第 6 节时序）────────────────────────────────────────────────────────

    /// <summary>
    /// 同一个工作单元里调用 DebitBalanceAsync，再把订单条件更新为 Paid。
    /// 更新影响 0 行则整个工作单元回滚（扣款不会留下），按第 3.2 节读状态后返回——
    /// 不在这里调用 ReverseDebitAsync。
    /// </summary>
    public virtual async Task<StartPayResult> PayWithBalanceAsync(OrdOrder order, OrdQuote quote)
    {
        AssertPayable(order, quote);

        if (await CancelPendingCollectionAsync(order))
        {
            return new StartPayResult
            {
                Outcome = StartPayOutcome.AlreadyCompleted,
                Order = (await ReloadAsync(order.Id))!,
            };
        }

        var now = _clock.Now;
        var rolledBack = false;
        // using 确保任何路径都释放（无 dispose 会泄漏 DbContext 作用域与连接）；
        // 0 行路径在 using 内回滚、在 using 外（另开的上下文）读状态——计划第 3.2 节
        ///「另开工作单元读当前状态」，不能在已回滚的工作单元里继续读和写
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            // 扣款与标支付同在这个 requiresNew 工作单元（真实实现按 Default 连接串加入同一事务）
            var debit = await _integrationService.DebitBalanceAsync(new AbpAdmin.Collection.Contracts.DebitBalanceInput
            {
                BusinessType = OrderingConsts.OrderBusinessType,
                BusinessId = order.Id,
                UserId = order.UserId,
                Currency = quote.PayMethod.BalanceCurrency()!.Value,
                Amount = AbpAdmin.Collection.Contracts.CollectionAmounts.Format(quote.PayAmount),
            });

            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var rows = await dbContext.Orders
                .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment
                    && o.CollectionId == order.CollectionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.Paid)
                    .SetProperty(o => o.PaidAt, now)
                    .SetProperty(o => o.PayAmount, quote.PayAmount)
                    .SetProperty(o => o.PayCurrency, quote.PayCurrency)
                    .SetProperty(o => o.PayChannel, quote.PayMethod)
                    .SetProperty(o => o.PayNetwork, OrderingConsts.BscNetworkName)
                    .SetProperty(o => o.QuoteId, quote.Id)
                    .SetProperty(o => o.BalanceTxnId, debit.BalanceTxnId));

            if (rows == 0)
            {
                // 整个（含扣款）回滚；状态读取与善后放到 using 之外
                await uow.RollbackAsync();
                rolledBack = true;
            }
            else
            {
                await uow.CompleteAsync();
                await PublishDebitedSafelyAsync(order.Id, order.UserId, debit.BalanceTxnId, quote);
            }
        }

        if (!rolledBack)
        {
            var paid = (await ReloadAsync(order.Id))!;
            // 并发发起的其他收款可能仍处于待支付，提交后再向渠道发起关单。
            await AfterCommitAsync(order.Id, () => CancelOtherPendingCollectionsAsync(order.Id, paid.CollectionId ?? Guid.Empty));
            return new StartPayResult { Outcome = StartPayOutcome.BalancePaid, Order = paid };
        }

        // 另开上下文读当前状态（using 已释放，读取落在环境工作单元上）
        var current = await ReloadAsync(order.Id);
        switch (current!.Status)
        {
            case OrderStatus.Paid when current.PayChannel == quote.PayMethod:
                // 已是 Paid 且归因（方式、金额、币种）与本次相同：这次调用当作成功
                return new StartPayResult { Outcome = StartPayOutcome.BalancePaid, Order = current };

            case OrderStatus.Paid:
                // 已是 Paid 且归因不同：写 PaymentAnomaly
                await AppendAnomalyAsync(current.Id, quote.PayAmount, quote.PayCurrency,
                    null, null, "Balance debit raced with another paying source; debit rolled back.");
                return new StartPayResult { Outcome = StartPayOutcome.AlreadyCompleted, Order = (await ReloadAsync(order.Id))! };

            case OrderStatus.Closed:
                return new StartPayResult { Outcome = StartPayOutcome.Closed, Order = current };

            case OrderStatus.PendingPayment when current.CollectionId != order.CollectionId:
                throw new BusinessException(CollectionErrorCodes.PendingCollectionConflict)
                    .WithData("BusinessId", order.Id);

            default:
                // 仍是 PendingPayment 的窗口极小（条件更新只认 PendingPayment）；按已支付处理时留异常
                await AppendAnomalyAsync(order.Id, quote.PayAmount, quote.PayCurrency,
                    null, null, "Conditional pay update returned 0 rows unexpectedly; debit rolled back.");
                return new StartPayResult { Outcome = StartPayOutcome.AlreadyCompleted, Order = (await ReloadAsync(order.Id))! };
        }
    }

    // ── 到账处理（计划第 3.2 节）──────────────────────────────────────────────────────────

    /// <summary>
    /// 渠道完成事件 / 取消返回已完成 / 补状态作业共用的到账规则。
    /// 金额和币种与订单的 PayAmount、PayCurrency 比较；金额或币种不一致只追加异常明细，
    /// 订单保持 PendingPayment，等到它自己的 ExpiresAt 再关闭（错额不提前关单）。
    /// </summary>
    public virtual async Task HandleCollectionCompletedAsync(
        Guid orderId,
        Guid collectionId,
        OrderingPayChannel sourceChannel,
        decimal amount,
        AbpAdmin.Collection.Contracts.CollectionCurrency currency,
        string? channelTradeNo,
        DateTime? completedAt = null)
    {
        using var uow = BeginPaymentHandling();
        var order = await LockOrderAsync(orderId);
        if (order == null)
        {
            Logger.LogWarning("到账事件指向不存在的订单 {OrderId}，忽略", orderId);
            return;
        }

        var paidAt = completedAt.GetValueOrDefault();
        await ApplyCollectionCompletionAsync(order, collectionId, sourceChannel, amount, currency, channelTradeNo,
            paidAt == default ? _clock.Now : paidAt);
        await uow.CompleteAsync();
    }

    public virtual Task HandleCompletedCollectionAsync(Guid orderId, CollectionDto collection) =>
        HandleCollectionCompletedAsync(orderId, collection.Id,
            OrderingPayChannelExtensions.FromCollectionChannel(collection.Channel),
            CollectionAmounts.Parse(collection.Amount), collection.Currency, collection.ChannelTradeNo, collection.CompletedAt);

    private async Task ApplyCollectionCompletionAsync(
        OrdOrder order, Guid collectionId, OrderingPayChannel sourceChannel, decimal amount,
        CollectionCurrency currency, string? channelTradeNo, DateTime completedAt)
    {
        var amountsMatch = order.PayAmount != null
            && order.PayCurrency == currency
            && order.PayAmount.Value == amount;

        switch (order.Status)
        {
            case OrderStatus.PendingPayment:
                if (!amountsMatch)
                {
                    // 只追加异常明细，不关单；关单留到订单自己的 ExpiresAt
                    await AppendCollectionAnomalyOnceAsync(order.Id, amount, currency, collectionId, channelTradeNo,
                        CollectionMismatchNote);
                    return;
                }

                if (order.CollectionId == collectionId)
                {
                    // 同一笔收款完成：标 Paid（条件更新），渠道单号随归因落库（用户对账凭证）
                    await MarkPaidFromChannelAsync(order.Id, collectionId, sourceChannel, channelTradeNo, completedAt);
                    return;
                }

                var marked = await MarkPaidFromChannelAsync(order.Id, collectionId, sourceChannel, channelTradeNo, completedAt);
                if (marked)
                {
                    await AfterCommitAsync(order.Id, () => CancelOtherPendingCollectionsAsync(order.Id, collectionId));
                }

                return;

            case OrderStatus.Paid:
            case OrderStatus.Fulfilled:
                // 同一笔渠道支付可重复投递，余额支付留下的旧渠道收款号不能用于去重。
                if (order.BalanceTxnId == null && order.CollectionId == collectionId)
                {
                    return;
                }

                // 另一笔收款在订单已支付后到达：追加一条异常明细，不覆盖已有明细，并告警
                await AppendCollectionAnomalyOnceAsync(order.Id, amount, currency, collectionId, channelTradeNo,
                    DuplicateCollectionNote);
                Logger.LogWarning("订单 {OrderId} 已支付后又收到收款 {CollectionId} 的完成", order.Id, collectionId);
                return;

            case OrderStatus.Closed:
                if (order.CloseReason == OrderCloseReason.Expired && !order.PaymentAnomaly && amountsMatch)
                {
                    // 超时关闭且没有任何异常明细、金额币种一致：标回 Paid，并写上这笔的归因
                    //（渠道单号一并落库——复活也是渠道版标 Paid，对账凭证不能缺）
                    await ReviveFromExpiredCloseAsync(order.Id, collectionId, sourceChannel, channelTradeNo, completedAt);
                    return;
                }

                if (!amountsMatch && order.CloseReason == OrderCloseReason.Expired)
                {
                    await AppendCollectionAnomalyOnceAsync(order.Id, amount, currency, collectionId, channelTradeNo,
                        CollectionMismatchNote);
                }

                Logger.LogWarning(
                    "已关闭订单 {OrderId} 收到收款 {CollectionId} 的完成，订单保持关闭（CloseReason={CloseReason}, AmountsMatch={AmountsMatch}）",
                    order.Id, collectionId, order.CloseReason, amountsMatch);
                return;

        }
    }

    /// <summary>
    /// 充值到账：插入已支付的充值订单（BalanceTxnId 唯一，幂等——已存在即跳过）。
    /// </summary>
    public virtual async Task HandleBalanceCreditedAsync(
        Guid balanceTxnId, Guid userId, AbpAdmin.Collection.Contracts.CollectionCurrency currency, decimal amount, DateTime creditedAt)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var exists = await dbContext.Orders.AnyAsync(o =>
            o.OrderType == OrderType.TopUp && o.BalanceTxnId == balanceTxnId);
        if (exists)
        {
            return;
        }

        var order = new OrdOrder(GuidGenerator.Create())
        {
            OrderNo = Services.OrderServiceHelpers.NextOrderNo(_clock),
            UserId = userId,
            OrderType = OrderType.TopUp,
            Subject = "TopUp",
            PayAmount = amount,
            PayCurrency = currency,
            PayChannel = currency == AbpAdmin.Collection.Contracts.CollectionCurrency.Usdt
                ? OrderingPayChannel.BalanceUsdt
                : OrderingPayChannel.BalanceUsdc,
            PayNetwork = OrderingConsts.BscNetworkName,
            Status = OrderStatus.Paid,
            PaidAt = creditedAt,
            BalanceTxnId = balanceTxnId,
            Currency = currency.ToString().ToUpperInvariant(),
            CreationTime = _clock.Now,
        };

        await dbContext.Orders.AddAsync(order);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException pg
                  && pg.SqlState == "23505"
                  && pg.ConstraintName != null
                  && pg.ConstraintName.Contains("BalanceTxnId"))
        {
            // 充值订单 BalanceTxnId 部分唯一索引兜底：事件重投/与补插作业并发的赢家已插入。
            // 只认这一个约束：同毫秒 OrderNo 撞号也是 23505，吞掉会把插入静默延迟一整轮
            dbContext.Entry(order).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }

    /// <summary>扣款事件到达时订单仍待支付，则记录异常，后续由补状态作业核对。</summary>
    public virtual async Task HandleBalanceDebitedAsync(
        Guid orderId, Guid balanceTxnId, decimal amount, AbpAdmin.Collection.Contracts.CollectionCurrency currency)
    {
        var order = await ReloadAsync(orderId);
        if (order == null)
        {
            Logger.LogWarning("扣款事件指向不存在的订单 {OrderId}，忽略", orderId);
            return;
        }

        if (order.Status == OrderStatus.PendingPayment)
        {
            await AppendAnomalyAsync(order.Id, amount, currency, null, null,
                "Balance debit committed but the order was not marked paid in the same unit of work.");
            Logger.LogWarning("订单 {OrderId} 收到扣款流水 {BalanceTxnId} 的完成事件但仍为待支付，已记录异常",
                orderId, balanceTxnId);
        }
    }

    // ── 关闭 / 履约 / 取消 ────────────────────────────────────────────────────────────────

    /// <summary>用户取消待支付订单前，先确认关联收款已关闭。</summary>
    public virtual async Task CancelByUserAsync(OrdOrder order)
    {
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus)
                .WithData("OrderId", order.Id)
                .WithData("Status", order.Status);
        }

        await CancelPendingCollectionAsync(order);

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment
                && o.CollectionId == order.CollectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Closed)
                .SetProperty(o => o.ClosedAt, _clock.Now)
                .SetProperty(o => o.CloseReason, OrderCloseReason.UserCancel));
    }

    /// <summary>过期关闭（补状态作业）：把仍为 PendingPayment 且已过 ExpiresAt 的订单关闭。</summary>
    public virtual async Task CloseExpiredAsync(OrdOrder order)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment
                && o.CollectionId == order.CollectionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Closed)
                .SetProperty(o => o.ClosedAt, _clock.Now)
                .SetProperty(o => o.CloseReason, OrderCloseReason.Expired));
    }

    /// <summary>把仍为 Paid 的业务单标为已履约。</summary>
    public virtual async Task MarkFulfilledAsync(OrdOrder order)
    {
        if (order.OrderType != OrderType.Business)
        {
            throw new BusinessException(OrderingErrorCodes.NotAFulfillableOrder)
                .WithData("OrderId", order.Id);
        }

        if (order.Status != OrderStatus.Paid)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus)
                .WithData("OrderId", order.Id)
                .WithData("Status", order.Status);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Fulfilled)
                .SetProperty(o => o.FulfilledAt, _clock.Now));
    }

    // ── 异常明细（只追加）────────────────────────────────────────────────────────────────

    /// <summary>追加支付明细。<paramref name="markAnomaly"/> 只用于错额、多笔到账等异常，常规退款不标异常。</summary>
    public virtual Task AppendAnomalyAsync(
        Guid orderId, decimal? amount, AbpAdmin.Collection.Contracts.CollectionCurrency? currency,
        Guid? collectionId, string? channelTradeNo, string note, bool markAnomaly = true) =>
        InsertPaymentDetailAsync(GuidGenerator.Create(), orderId, amount, currency, collectionId, channelTradeNo,
            note, _clock.Now, markAnomaly);

    private async Task InsertPaymentDetailAsync(
        Guid detailId, Guid orderId, decimal? amount, CollectionCurrency? currency,
        Guid? collectionId, string? channelTradeNo, string note, DateTime creationTime, bool markAnomaly)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        await dbContext.PaymentAnomalyLines.AddAsync(new OrdPaymentAnomalyLine(detailId)
        {
            OrderId = orderId,
            Amount = amount,
            Currency = currency,
            CollectionId = collectionId,
            ChannelTradeNo = channelTradeNo,
            Note = note,
            CreationTime = creationTime,
        });

        if (markAnomaly)
        {
            await dbContext.Orders
                .Where(o => o.Id == orderId && !o.PaymentAnomaly)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentAnomaly, true));
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task AppendCollectionAnomalyOnceAsync(
        Guid orderId, decimal amount, CollectionCurrency currency, Guid collectionId, string? channelTradeNo, string note)
    {
        // 所有渠道完成入口持有同一个订单行锁。作业补偿与事件重投共享按收款单的去重范围。
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        if (await dbContext.PaymentAnomalyLines.AnyAsync(line =>
                line.OrderId == orderId && line.CollectionId == collectionId
                && (line.Note == CollectionMismatchNote || line.Note == DuplicateCollectionNote)))
        {
            return;
        }

        await AppendAnomalyAsync(orderId, amount, currency, collectionId, channelTradeNo, note);
    }

    public virtual async Task HandleCollectionRefundSucceededAsync(CollectionRefundSucceededEto refund)
    {
        using var uow = BeginPaymentHandling();
        var order = await LockOrderAsync(refund.BusinessId);
        if (order == null)
        {
            Logger.LogWarning("退款 {RefundId} 指向不存在的订单 {OrderId}", refund.RefundId, refund.BusinessId);
            return;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        if (!await dbContext.PaymentAnomalyLines.AnyAsync(line => line.Id == refund.RefundId))
        {
            // 退款按原渠道实付币种记录。订单上的人工退款累计使用标价币种，不能混加。
            await InsertPaymentDetailAsync(refund.RefundId, order.Id, CollectionAmounts.Parse(refund.Amount),
                refund.Currency, refund.CollectionId, refund.ChannelRefundNo,
                "Original-channel refund completed.", refund.SucceededAt, markAnomaly: false);
        }

        await uow.CompleteAsync();
    }

    /// <summary>
    /// 记录人工退款，空金额只保存说明。调用方校验金额和币种，并用事务一起提交退款明细和累计金额。
    /// </summary>
    public virtual async Task AppendManualRefundAsync(
        Guid orderId, decimal? amount, AbpAdmin.Collection.Contracts.CollectionCurrency? currency, string note)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        await AppendAnomalyAsync(orderId, amount, currency, null, null,
            "Manual refund record: " + note, markAnomaly: false);

        if (amount.HasValue)
        {
            await dbContext.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    o => o.RefundedAmount,
                    // 两参重载：第二个 lambda 逐行计算（列引用 o 在这个作用域才有效）
                    o => (o.RefundedAmount ?? 0m) + amount.Value));
        }
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>取消该订单当前指向的 Pending 收款。返回 true 表示渠道已完成、到账已按规则处理。</summary>
    private async Task<bool> CancelPendingCollectionAsync(OrdOrder order)
    {
        if (order.CollectionId == null)
        {
            return false;
        }

        // 先在扣款/写单事务外取消（渠道关单的 HTTP 不进任何数据库事务，计划第 6 节）
        var cancel = await _integrationService.CancelAsync(order.CollectionId.Value);
        if (cancel.Collection.Status == AbpAdmin.Collection.Contracts.CollectionStatus.Completed)
        {
            // 返回已完成：按金额币种走支付或异常，不再创建新收款、不扣款
            await HandleCompletedCollectionAsync(order.Id, cancel.Collection);
            return true;
        }

        if (cancel.Collection.Status == CollectionStatus.Pending)
        {
            throw new BusinessException(CollectionErrorCodes.PendingCollectionConflict)
                .WithData("BusinessId", order.Id)
                .WithData("CollectionId", cancel.Collection.Id);
        }

        return false;
    }

    /// <summary>订单和扣款提交后发布给消费者核对。发布失败只记录告警。</summary>
    private async Task PublishDebitedSafelyAsync(Guid orderId, Guid userId, Guid balanceTxnId, OrdQuote quote)
    {
        try
        {
            await _distributedEventBus.PublishAsync(new AbpAdmin.Collection.Contracts.BalanceDebitedEto
            {
                BusinessType = AbpAdmin.Ordering.OrderingConsts.OrderBusinessType,
                BusinessId = orderId,
                BalanceTxnId = balanceTxnId,
                UserId = userId,
                Currency = quote.PayCurrency,
                Amount = AbpAdmin.Collection.Contracts.CollectionAmounts.Format(quote.PayAmount),
                CompletedAt = _clock.Now,
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "订单 {OrderId} 的扣款与支付状态已提交，扣款事件发布失败", orderId);
        }
    }

    private async Task AfterCommitAsync(Guid orderId, Func<Task> action)
    {
        var current = _unitOfWorkManager.Current;
        if (current == null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "订单 {OrderId} 已支付，关闭其他待支付收款失败", orderId);
            }

            return;
        }

        current.OnCompleted(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "订单 {OrderId} 已支付，关闭其他待支付收款失败", orderId);
            }
        });
    }

    /// <summary>条件更新标 Paid（WHERE Status = 'PendingPayment'）。返回是否生效。</summary>
    private async Task<bool> MarkPaidFromChannelAsync(
        Guid orderId, Guid collectionId, OrderingPayChannel channel, string? channelTradeNo, DateTime completedAt)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, completedAt)
                .SetProperty(o => o.CollectionId, collectionId)
                .SetProperty(o => o.PayChannel, channel)
                // 法币渠道无网络概念（PayNetwork 保持 null）；将来若加链上直付渠道，
                // 由渠道定义携带网络名写入（与余额路径的 OrderingConsts.BscNetworkName 同口径）
                .SetProperty(o => o.ChannelTradeNo, channelTradeNo));
        return rows > 0;
    }

    /// <summary>
    /// 「已扣未标支付」补标（计划第 3.3 节）：金额币种核对由调用方完成，这里只做条件更新
    /// PendingPayment → Paid 并写余额归因（PayAmount/PayCurrency 已是核对过的应付，不重写）。
    /// 返回是否真的更新（0 行 = 状态已被并发改变，作业下一轮再看）。
    /// </summary>
    public virtual async Task<bool> MarkPaidFromDebitAsync(
        Guid orderId, Guid balanceTxnId, AbpAdmin.Collection.Contracts.CollectionCurrency currency)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, _clock.Now)
                .SetProperty(o => o.PayChannel, currency == AbpAdmin.Collection.Contracts.CollectionCurrency.Usdc
                    ? OrderingPayChannel.BalanceUsdc
                    : OrderingPayChannel.BalanceUsdt)
                .SetProperty(o => o.PayNetwork, OrderingConsts.BscNetworkName)
                .SetProperty(o => o.BalanceTxnId, balanceTxnId));
        return rows > 0;
    }

    /// <summary>
    /// 过期关闭订单的余额版复活（计划第 3.3 节）：与 <see cref="ReviveFromExpiredCloseAsync"/> 同一守卫
    ///（Closed + Expired + 无异常），写余额归因而不是收款归因。两个复活必须保持同族条件，
    /// 所以都住在这里——作业不自己拼状态转移（防状态机泄漏）。
    /// </summary>
    public virtual async Task<bool> ReviveFromDebitAsync(
        Guid orderId, decimal amount, AbpAdmin.Collection.Contracts.CollectionCurrency currency, Guid balanceTxnId)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == orderId
                && o.Status == OrderStatus.Closed
                && o.CloseReason == OrderCloseReason.Expired
                && !o.PaymentAnomaly)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, _clock.Now)
                .SetProperty(o => o.PayAmount, amount)
                .SetProperty(o => o.PayCurrency, currency)
                .SetProperty(o => o.PayChannel, currency == AbpAdmin.Collection.Contracts.CollectionCurrency.Usdc
                    ? OrderingPayChannel.BalanceUsdc
                    : OrderingPayChannel.BalanceUsdt)
                .SetProperty(o => o.PayNetwork, OrderingConsts.BscNetworkName)
                .SetProperty(o => o.BalanceTxnId, balanceTxnId));
        return rows > 0;
    }

    /// <summary>
    /// 超时关闭订单的复活：条件更新把 CloseReason = Expired、无异常、仍 Closed 的行标回 Paid
    ///（把 CloseReason/PaymentAnomaly 放进 WHERE，避免与并发的异常写入竞争，计划第 3.2 节）。
    /// </summary>
    private async Task ReviveFromExpiredCloseAsync(
        Guid orderId, Guid collectionId, OrderingPayChannel channel, string? channelTradeNo, DateTime completedAt)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == orderId
                && o.Status == OrderStatus.Closed
                && o.CloseReason == OrderCloseReason.Expired
                && !o.PaymentAnomaly)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, completedAt)
                .SetProperty(o => o.CollectionId, collectionId)
                .SetProperty(o => o.PayChannel, channel)
                .SetProperty(o => o.ChannelTradeNo, channelTradeNo));

        if (rows == 0)
        {
            Logger.LogWarning("订单 {OrderId} 当前已不满足超时关闭后补记支付的条件，未更新状态", orderId);
        }
    }

    private async Task CancelOtherPendingCollectionsAsync(Guid orderId, Guid paidCollectionId)
    {
        var pendings = await _integrationService.ListByBusinessAsync(OrderingConsts.OrderBusinessType, orderId);
        foreach (var pending in pendings.Where(c =>
                     c.Id != paidCollectionId && c.Status == AbpAdmin.Collection.Contracts.CollectionStatus.Pending))
        {
            await _integrationService.CancelAsync(pending.Id);
        }
    }

    private static void AssertPayable(OrdOrder order, OrdQuote quote)
    {
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidOrderStatus)
                .WithData("OrderId", order.Id)
                .WithData("Status", order.Status);
        }

        if (quote.OrderId != order.Id || quote.Status != QuoteStatus.Active)
        {
            throw new BusinessException(OrderingErrorCodes.InvalidQuoteConfig)
                .WithData("OrderId", order.Id);
        }
    }

    private async Task<OrdOrder?> ReloadAsync(Guid orderId)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
    }

    private IUnitOfWork BeginPaymentHandling() =>
        _unitOfWorkManager.Begin(requiresNew: _unitOfWorkManager.Current?.Options.IsTransactional != true,
            isTransactional: true);

    private async Task<OrdOrder?> LockOrderAsync(Guid orderId)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var orders = await dbContext.Orders
            .FromSqlInterpolated($"SELECT * FROM \"OrdOrders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync();
        return orders.SingleOrDefault();
    }

}
