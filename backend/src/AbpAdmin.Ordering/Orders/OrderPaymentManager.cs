using System;
using System.Linq;
using System.Threading.Tasks;
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
/// 订单支付状态机（计划第 3.2、6 节）。状态更新都是一条条件更新（WHERE Status = 'PendingPayment'）；
/// 余额支付的扣款和这次更新在同一个工作单元里，一起提交或一起回滚——
/// 回冲不是「更新失败了再调一次」；受影响行数为 0 时回滚这个工作单元（扣款不会留下），
/// 然后另开工作单元读当前状态决定语义，不在这里调用 ReverseDebitAsync。
/// 渠道调用（取消、创建）一律在扣款所在的工作单元打开之前完成。
/// </summary>
public class OrderPaymentManager : DomainService
{
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
    /// 若已有 Pending 收款，先取消（渠道 HTTP 在扣款事务外，这里尚未打开任何事务）；
    /// 返回已完成则按到账处理，不再创建新收款。
    /// </summary>
    public virtual async Task<StartPayResult> StartChannelPayAsync(OrdOrder order, OrdQuote quote)
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

        var created = await _integrationService.CreateAsync(new AbpAdmin.Collection.Contracts.CreateCollectionInput
        {
            BusinessType = OrderingConsts.OrderBusinessType,
            BusinessId = order.Id,
            UserId = order.UserId,
            Channel = quote.PayMethod.ToCollectionChannel()!.Value,
            Currency = quote.PayCurrency,
            Amount = AbpAdmin.Collection.Contracts.CollectionAmounts.Format(quote.PayAmount),
            ExpiresAt = quote.ExpiresAt,
        });

        // PayAmount/PayCurrency/PayChannel/QuoteId/CollectionId 一起写入（每次发起支付时按当前报价）；
        // 条件更新保证只在 PendingPayment 时生效。0 行 = 发起过程中订单被并发关闭/支付：
        // 收款单留给过期作业取消，按当前状态如实回报，不谎报 ChannelCreated（评审 L 级）
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PayAmount, quote.PayAmount)
                .SetProperty(o => o.PayCurrency, quote.PayCurrency)
                .SetProperty(o => o.PayChannel, quote.PayMethod)
                .SetProperty(o => o.QuoteId, quote.Id)
                .SetProperty(o => o.CollectionId, created.Id));

        if (rows == 0)
        {
            var current = (await ReloadAsync(order.Id))!;
            return new StartPayResult
            {
                Outcome = current.Status switch
                {
                    OrderStatus.Paid => StartPayOutcome.AlreadyCompleted,
                    OrderStatus.Closed => StartPayOutcome.Closed,
                    _ => StartPayOutcome.ChannelCreated, // 窗口极小；下一轮条件更新为准
                },
                Collection = created,
                Order = current,
            };
        }

        return new StartPayResult
        {
            Outcome = StartPayOutcome.ChannelCreated,
            Collection = created,
            Order = await ReloadAsync(order.Id),
        };
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
        // using 确保任何路径都释放（评审 M 级：无 dispose 会泄漏 DbContext 作用域与连接）；
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
                .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment)
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
                // 扣款成功且与标支付同一工作单元提交之后发布（计划第 3.2 节）
                await PublishDebitedSafelyAsync(order.Id, order.UserId, debit.BalanceTxnId, quote);
            }
        }

        if (!rolledBack)
        {
            var paid = (await ReloadAsync(order.Id))!;
            // 与渠道路径同款善后：标支付提交后尽力取消其余等待中的收款（计划第 3.2 节；
            ///余额单最容易留下悬空 Pending 收款——并发渠道发起败给余额支付时）
            await AfterCommitAsync(() => CancelOtherPendingCollectionsAsync(order.Id, paid.CollectionId ?? Guid.Empty));
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
        string? channelTradeNo)
    {
        var order = await ReloadAsync(orderId);
        if (order == null)
        {
            Logger.LogWarning("到账事件指向不存在的订单 {OrderId}，忽略", orderId);
            return;
        }

        var amountsMatch = order.PayAmount != null
            && order.PayCurrency == currency
            && order.PayAmount.Value == amount;

        switch (order.Status)
        {
            case OrderStatus.PendingPayment:
                if (!amountsMatch)
                {
                    // 只追加异常明细，不关单；关单留到订单自己的 ExpiresAt
                    await AppendAnomalyAsync(order.Id, amount, currency, collectionId, channelTradeNo,
                        "Completed collection amount or currency mismatch.");
                    return;
                }

                if (order.CollectionId == collectionId)
                {
                    // 同一笔收款完成：标 Paid（条件更新），渠道单号随归因落库（用户对账凭证）
                    await MarkPaidFromChannelAsync(order.Id, collectionId, sourceChannel, channelTradeNo);
                    return;
                }

                // 来源不同且订单仍是 PendingPayment、金额币种一致：这笔成为致付来源，
                // 更新 CollectionId 与 PayChannel，写下 PaidAt，标 Paid（计划第 3.2 节）
                var marked = await MarkPaidFromChannelAsync(order.Id, collectionId, sourceChannel, channelTradeNo);
                if (marked)
                {
                    // 提交之后再尽力取消其余 Pending 收款，失败由过期作业兜底，
                    // 不在这个工作单元里调用渠道（OnCompleted 已在事务之外）
                    await AfterCommitAsync(() => CancelOtherPendingCollectionsAsync(order.Id, collectionId));
                }

                return;

            case OrderStatus.Paid:
                // 同一来源再投递，不改状态：仅当订单由渠道收款支付且致付归因正是这一笔。
                // 余额支付的单（BalanceTxnId 非空）即使残留旧的 CollectionId（换渠道前的
                ///渠道尝试），其后的任何渠道完成都是「来源不同且已支付」——按计划第 3.2 节
                ///必须追加异常并告警，不能吞掉（评审 H 级：渠道实际付款后无人记账）
                if (order.BalanceTxnId == null && order.CollectionId == collectionId)
                {
                    return;
                }

                // 另一笔收款在订单已支付后到达：追加一条异常明细，不覆盖已有明细，并告警
                await AppendAnomalyAsync(order.Id, amount, currency, collectionId, channelTradeNo,
                    "A second completed collection arrived after the order was paid.");
                Logger.LogWarning("订单 {OrderId} 已支付后又收到收款 {CollectionId} 的完成", orderId, collectionId);
                return;

            case OrderStatus.Closed:
                if (order.CloseReason == OrderCloseReason.Expired && !order.PaymentAnomaly && amountsMatch)
                {
                    // 超时关闭且没有任何异常明细、金额币种一致：标回 Paid，并写上这笔的归因
                    //（渠道单号一并落库——复活也是渠道版标 Paid，对账凭证不能缺）
                    await ReviveFromExpiredCloseAsync(order.Id, collectionId, sourceChannel, channelTradeNo);
                    return;
                }

                // CloseReason 为 UserCancel，或已有异常明细：不改状态，只告警
                Logger.LogWarning(
                    "已关闭订单 {OrderId}（CloseReason={CloseReason}, Anomaly={Anomaly}）收到收款 {CollectionId} 的完成，只告警不改状态",
                    orderId, order.CloseReason, order.PaymentAnomaly, collectionId);
                return;

            case OrderStatus.Fulfilled:
                // 本期没有从 Fulfilled 出发的状态转移
                Logger.LogWarning("已履约订单 {OrderId} 收到收款 {CollectionId} 的完成，忽略", orderId, collectionId);
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
            // 只认这一个约束：同毫秒 OrderNo 撞号也是 23505，吞掉会把插入静默延迟一整轮（评审 M 级）
            dbContext.Entry(order).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }

    /// <summary>
    /// BalanceDebitedEto 到达时订单应已是 Paid。若仍是 PendingPayment，说明扣款没有和标支付
    /// 放在同一个工作单元里：记异常并不改状态，交给补状态作业（计划第 3.2 节）。
    /// </summary>
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
            Logger.LogWarning("订单 {OrderId} 收到扣款完成事件但仍为待支付，交给补状态作业", orderId);
        }
    }

    // ── 关闭 / 履约 / 取消 ────────────────────────────────────────────────────────────────

    /// <summary>用户在页面上取消：只有 PendingPayment 可以；先尽力取消等待中的收款，再关单。</summary>
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
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment)
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
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Closed)
                .SetProperty(o => o.ClosedAt, _clock.Now)
                .SetProperty(o => o.CloseReason, OrderCloseReason.Expired));
    }

    /// <summary>
    /// 管理端动作：把业务单从 Paid 标成 Fulfilled。标了之后不能回冲，退款只做人工记录（计划第 1 节）。
    /// </summary>
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

    /// <summary>
    /// 追加异常明细；订单的 PaymentAnomaly 标记写入一次、不覆盖。明细永不更新。
    /// </summary>
    /// <summary>追加一条明细；<paramref name="markAnomaly"/> 控制是否点亮订单的 PaymentAnomaly
    /// 异常标——到账类异常（金额不符/多笔到账）要点亮；人工退款走
    /// <see cref="AppendManualRefundAsync"/>（markAnomaly=false）：退款是常规运营动作，
    /// 不打异常标（否则干净支付的用户订单永远挂红标，复活守卫 !PaymentAnomaly 的语义也被污染）。</summary>
    public virtual async Task AppendAnomalyAsync(
        Guid orderId, decimal? amount, AbpAdmin.Collection.Contracts.CollectionCurrency? currency,
        Guid? collectionId, string? channelTradeNo, string note, bool markAnomaly = true)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        await dbContext.PaymentAnomalyLines.AddAsync(new OrdPaymentAnomalyLine(GuidGenerator.Create())
        {
            OrderId = orderId,
            Amount = amount,
            Currency = currency,
            CollectionId = collectionId,
            ChannelTradeNo = channelTradeNo,
            Note = note,
            CreationTime = _clock.Now,
        });

        if (markAnomaly)
        {
            await dbContext.Orders
                .Where(o => o.Id == orderId && !o.PaymentAnomaly)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentAnomaly, true));
        }

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 人工退款记录（管理端动作的落地）：追加一条退款明细（不打异常标）+ 累加 RefundedAmount。
    /// 金额为 null 表示只记说明不记金额（此时不累加）。币种与两位小数由调用方
    /// RecordManualRefundAsync 校验（必须等于订单标价币种），保证
    /// RefundedAmount = Σ 退款明细金额且币种单一。用户侧订单详情可见——退款不再只是管理端口径。
    /// 契约：明细插入与累加是两条语句，原子性依赖调用方的环境事务
    /// （Auto API 的 POST 环境工作单元是事务性的；新调用方若非事务须自行包 UoW）。
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
            await HandleCollectionCompletedAsync(
                order.Id,
                cancel.Collection.Id,
                OrderingPayChannelExtensions.FromCollectionChannel(cancel.Collection.Channel),
                AbpAdmin.Collection.Contracts.CollectionAmounts.Parse(cancel.Collection.Amount),
                cancel.Collection.Currency,
                cancel.Collection.ChannelTradeNo);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 扣款完成事件发布（计划第 3.2 节）：只在「扣款 + 标支付同事务已提交」之后调用；
    /// 发布失败只告警——事件的消费端只是自检快路径，兜底在补状态作业。
    /// </summary>
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
            Logger.LogWarning(ex, "BalanceDebitedEto 发布失败（订单 {OrderId}），补状态作业会兜底", orderId);
        }
    }

    private async Task AfterCommitAsync(Func<Task> action)    {
        var current = _unitOfWorkManager.Current;
        if (current == null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "标支付后的善后动作失败，交给过期作业兜底");
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
                Logger.LogWarning(ex, "标支付后的善后动作失败（已提交），交给过期作业兜底");
            }
        });
    }

    /// <summary>条件更新标 Paid（WHERE Status = 'PendingPayment'）。返回是否生效。</summary>
    private async Task<bool> MarkPaidFromChannelAsync(
        Guid orderId, Guid collectionId, OrderingPayChannel channel, string? channelTradeNo)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, _clock.Now)
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
    /// 所以都住在这里——作业不自己拼状态转移（评审 M 级：状态机泄漏）。
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
        Guid orderId, Guid collectionId, OrderingPayChannel channel, string? channelTradeNo)
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
                .SetProperty(o => o.CollectionId, collectionId)
                .SetProperty(o => o.PayChannel, channel)
                .SetProperty(o => o.ChannelTradeNo, channelTradeNo));

        if (rows == 0)
        {
            Logger.LogWarning("订单 {OrderId} 复活条件更新影响 0 行（状态已被并发改变），保留现状", orderId);
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

}
