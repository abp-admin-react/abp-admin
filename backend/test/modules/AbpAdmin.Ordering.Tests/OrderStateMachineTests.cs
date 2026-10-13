using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// 订单状态机用例（计划第 8 节 M1 子集）：集成服务换成桩（ReplaceServices），
/// 不启链、不启渠道。断言点全部对应计划条文：取消旧收款不关闭订单；CancelAsync 返回
/// 已完成则不建新单、不扣款；同一来源重复不改状态；第二笔到账只追加明细；迟到的另一笔
/// 成为致付来源；用户取消后的到账只告警；超时关闭且异常为空时匹配到账标回已支付；
/// 已写过异常的超时关闭订单不复活；金额不一致只追加明细（错额不提前关单）。
/// </summary>
public class OrderStateMachineTests : OrderingStubTestBase
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// 领域服务直接调用时 DbContextProvider 需要环境工作单元；测试里用非事务 UoW 包住每步
    ///（状态机自身的事务边界由 OrderPaymentManager 显式控制，不受外层影响）。
    /// </summary>
    private async Task UowAsync(Func<Task> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        await action();
        await uow.CompleteAsync();
    }

    private async Task<T> UowAsync<T>(Func<Task<T>> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var result = await action();
        await uow.CompleteAsync();
        return result;
    }

    [Fact]
    public async Task StartChannelPay_Writes_Quote_Fields_And_Creates_Collection()
    {
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        var result = await UowAsync(async () =>
        {
            var order = await CreateBusinessOrderAsync();
            var quote = await quoteManager.GetOrCreateActiveQuoteAsync(order, OrderingPayChannel.WechatNative);
            return await manager.StartChannelPayAsync(order, quote);
        });

        result.Outcome.ShouldBe(StartPayOutcome.ChannelCreated);
        result.Collection.ShouldNotBeNull();
        result.Collection.ChannelPayload.ShouldNotBeNullOrEmpty();
        Stub.CreateCallCount.ShouldBe(1);

        var current = await UowAsync(() => ReloadAsync(result.Order.Id));
        current.Status.ShouldBe(OrderStatus.PendingPayment); // 渠道支付不在这里标 Paid
        current.PayChannel.ShouldBe(OrderingPayChannel.WechatNative);
        current.PayCurrency.ShouldBe(CollectionCurrency.Cny);
        current.PayAmount.ShouldNotBeNull();
        current.CollectionId.ShouldBe(result.Collection.Id);
    }

    [Fact]
    public async Task Matching_Completion_Marks_Paid_With_Attribution()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;
        var collectionId = current.CollectionId!.Value;

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, collectionId,
                OrderingPayChannel.WechatNative, pay, CollectionCurrency.Cny, "wx-trade-1"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Paid);
        after.PaidAt.ShouldNotBeNull();
        after.CollectionId.ShouldBe(collectionId);
    }

    [Fact]
    public async Task Same_Source_ReDelivery_Does_Not_Change_State()
    {
        var order = await MarkPaidViaWechatAsync();
        var first = await UowAsync(() => ReloadAsync(order.Id));

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, first.CollectionId!.Value,
                OrderingPayChannel.WechatNative, first.PayAmount!.Value, CollectionCurrency.Cny, "wx-trade-1"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.PaidAt.ShouldBe(first.PaidAt); // 同一来源再投递，不改状态
    }

    [Fact]
    public async Task Mismatched_Amount_Only_Appends_Anomaly_Order_Stays_Pending()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;

        // 金额不一致：只追加异常明细，订单保持待支付（错额不提前关单，关单留到订单自己的 ExpiresAt）
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, Guid.NewGuid(),
                OrderingPayChannel.WechatNative, pay + 10m, CollectionCurrency.Cny, "wx-bad"));

        var mid = await UowAsync(() => ReloadAsync(order.Id));
        mid.Status.ShouldBe(OrderStatus.PendingPayment);
        mid.PaymentAnomaly.ShouldBeTrue();
        (await UowAsync(() => AnomalyLinesAsync(order.Id))).Count.ShouldBe(1);

        // 错额之后金额正确的到账仍可支付
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, mid.CollectionId!.Value,
                OrderingPayChannel.WechatNative, pay, CollectionCurrency.Cny, "wx-good"));
        (await UowAsync(() => ReloadAsync(order.Id))).Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task Second_Arrival_After_Paid_Appends_Line_Does_Not_Overwrite()
    {
        var order = await MarkPaidViaWechatAsync();

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, Guid.NewGuid(),
                OrderingPayChannel.AlipayPrecreate, 100m, CollectionCurrency.Cny, "ali-2"));
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, Guid.NewGuid(),
                OrderingPayChannel.StripeCheckout, 200m, CollectionCurrency.Usd, "stripe-3"));

        var lines = await UowAsync(() => AnomalyLinesAsync(order.Id));
        lines.Count.ShouldBe(2); // 只追加，不覆盖：第二条完整保留
        (await UowAsync(() => ReloadAsync(order.Id))).Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task Late_Other_Channel_Becomes_Paying_Source()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;

        // 待支付时迟到的另一笔（金额币种一致）成为致付来源：更新 CollectionId 与 PayChannel
        var late = Guid.NewGuid();
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, late,
                OrderingPayChannel.AlipayPrecreate, pay, CollectionCurrency.Cny, "ali-late"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Paid);
        after.CollectionId.ShouldBe(late);
        after.PayChannel.ShouldBe(OrderingPayChannel.AlipayPrecreate);
    }

    [Fact]
    public async Task User_Cancel_Then_Arrival_Only_Warns()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;
        var manager = GetRequiredService<OrderPaymentManager>();

        await UowAsync(() => manager.CancelByUserAsync(current));

        // 用户取消后的到账只告警：不改状态、不复活、不写异常明细
        await UowAsync(() => manager.HandleCollectionCompletedAsync(order.Id, Guid.NewGuid(),
            OrderingPayChannel.WechatNative, pay, CollectionCurrency.Cny, "wx-late"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Closed);
        after.CloseReason.ShouldBe(OrderCloseReason.UserCancel);
        (await UowAsync(() => AnomalyLinesAsync(order.Id))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Expired_Close_With_Matching_Arrival_Revives()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var manager = GetRequiredService<OrderPaymentManager>();

        await UowAsync(() => manager.CloseExpiredAsync(current));

        // 超时关闭且没有任何异常明细、金额币种一致：标回 Paid 并写归因
        await UowAsync(() => manager.HandleCollectionCompletedAsync(order.Id, current.CollectionId!.Value,
            OrderingPayChannel.WechatNative, current.PayAmount!.Value, CollectionCurrency.Cny, "wx-1"));

        var revived = await UowAsync(() => ReloadAsync(order.Id));
        revived.Status.ShouldBe(OrderStatus.Paid);
        revived.CollectionId.ShouldBe(current.CollectionId);
    }

    [Fact]
    public async Task Expired_Close_With_Anomaly_Does_Not_Revive()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;
        var manager = GetRequiredService<OrderPaymentManager>();

        // 先写异常（错额到账），再超时关闭 → 已有异常明细，不复活
        await UowAsync(() => manager.HandleCollectionCompletedAsync(order.Id, Guid.NewGuid(),
            OrderingPayChannel.WechatNative, pay + 1m, CollectionCurrency.Cny, "wx-bad"));
        var closed = await UowAsync(() => ReloadAsync(order.Id));
        await UowAsync(() => manager.CloseExpiredAsync(closed));
        await UowAsync(() => manager.HandleCollectionCompletedAsync(order.Id, Guid.NewGuid(),
            OrderingPayChannel.WechatNative, pay, CollectionCurrency.Cny, "wx-good"));

        (await UowAsync(() => ReloadAsync(order.Id))).Status.ShouldBe(OrderStatus.Closed);
    }

    [Fact]
    public async Task Channel_Swap_Cancels_Old_And_Creates_New_Without_Closing_Order()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        var result = await UowAsync(async () =>
        {
            var fresh = await ReloadAsync(order.Id);
            var quote = await quoteManager.GetOrCreateActiveQuoteAsync(fresh, OrderingPayChannel.AlipayPrecreate);
            return await manager.StartChannelPayAsync(fresh, quote);
        });

        // 换渠道：旧收款被取消（桩默认 Closed），订单不关闭，新收款创建
        result.Outcome.ShouldBe(StartPayOutcome.ChannelCreated);
        Stub.CancelCallCount.ShouldBe(1);
        Stub.CreateCallCount.ShouldBe(2);

        var current = await UowAsync(() => ReloadAsync(order.Id));
        current.Status.ShouldBe(OrderStatus.PendingPayment); // 取消旧收款不关闭订单
        current.PayChannel.ShouldBe(OrderingPayChannel.AlipayPrecreate);
        current.CollectionId.ShouldBe(result.Collection!.Id);
    }

    [Fact]
    public async Task Cancel_Returning_Completed_Marks_Paid_No_New_Collection_No_Debit()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;

        // 换渠道时 CancelAsync 返回已完成：按金额币种标 Paid，不建新单、不扣款
        Stub.CancelOutcomes[current.CollectionId!.Value] = CollectionStatus.Completed;
        var dto = await Stub.GetAsync(current.CollectionId.Value);
        dto.ShouldNotBeNull();
        dto.Amount = CollectionAmounts.Format(pay);
        dto.Currency = CollectionCurrency.Cny;
        dto.Channel = CollectionChannel.WechatNative;

        var createsBefore = Stub.CreateCallCount;
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        var result = await UowAsync(async () =>
        {
            var fresh = await ReloadAsync(order.Id);
            var quote = await quoteManager.GetOrCreateActiveQuoteAsync(fresh, OrderingPayChannel.AlipayPrecreate);
            return await manager.StartChannelPayAsync(fresh, quote);
        });

        result.Outcome.ShouldBe(StartPayOutcome.AlreadyCompleted);
        Stub.CreateCallCount.ShouldBe(createsBefore); // 不再创建新收款
        Stub.DebitCallCount.ShouldBe(0); // 不扣款
        (await UowAsync(() => ReloadAsync(order.Id))).Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public async Task Balance_Pay_Marks_Paid_With_Txn_Attribution()
    {
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        var result = await UowAsync(async () =>
        {
            var order = await CreateBusinessOrderAsync();
            var quote = await quoteManager.GetOrCreateActiveQuoteAsync(order, OrderingPayChannel.BalanceUsdt);
            return await manager.PayWithBalanceAsync(order, quote);
        });

        result.Outcome.ShouldBe(StartPayOutcome.BalancePaid);
        Stub.DebitCallCount.ShouldBe(1);

        var current = await UowAsync(() => ReloadAsync(result.Order.Id));
        current.Status.ShouldBe(OrderStatus.Paid);
        current.BalanceTxnId.ShouldNotBeNull();
        current.PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdt);
        current.PayCurrency.ShouldBe(CollectionCurrency.Usdt);
        current.PayNetwork.ShouldBe("BSC");   // 渠道模型：余额支付主路径也写来源网络
    }

    [Fact]
    public async Task Balance_Pay_Rolls_Back_When_Order_Already_Closed()
    {
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        // 内存里的过期对象：创建时拿到（PendingPayment），随后库里被关闭
        var stale = await UowAsync(() => CreateBusinessOrderAsync());
        var quote = await UowAsync(() => quoteManager.GetOrCreateActiveQuoteAsync(stale, OrderingPayChannel.BalanceUsdt));

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            await dbContext.Orders.Where(o => o.Id == stale.Id && o.Status == OrderStatus.PendingPayment)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.Closed)
                    .SetProperty(o => o.CloseReason, OrderCloseReason.Expired));
            await uow.CompleteAsync();
        }

        // 用过期对象发起（对象上仍是 PendingPayment，库里已关闭）：条件更新影响 0 行 →
        // 整个工作单元回滚（扣款不会留下），按第 3.2 节读状态后返回 Closed
        var result = await UowAsync(() => manager.PayWithBalanceAsync(stale, quote));

        result.Outcome.ShouldBe(StartPayOutcome.Closed);
        var current = await UowAsync(() => ReloadAsync(stale.Id));
        current.Status.ShouldBe(OrderStatus.Closed);
        current.BalanceTxnId.ShouldBeNull();
    }

    [Fact]
    public async Task Balance_Credited_Inserts_TopUp_Order_Idempotently()
    {
        var manager = GetRequiredService<OrderPaymentManager>();
        var txnId = Guid.NewGuid();

        await UowAsync(() => manager.HandleBalanceCreditedAsync(txnId, UserId, CollectionCurrency.Usdt, 12.5m, DateTime.UtcNow));
        await UowAsync(() => manager.HandleBalanceCreditedAsync(txnId, UserId, CollectionCurrency.Usdt, 12.5m, DateTime.UtcNow));

        var topups = await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            return await dbContext.Orders.IgnoreQueryFilters()
                .Where(o => o.OrderType == OrderType.TopUp && o.BalanceTxnId == txnId)
                .ToListAsync();
        });
        topups.Count.ShouldBe(1); // BalanceTxnId 唯一：重复事件只插入一次
        topups[0].Status.ShouldBe(OrderStatus.Paid);
        topups[0].PayAmount.ShouldBe(12.5m);
        topups[0].PayCurrency.ShouldBe(CollectionCurrency.Usdt);
    }

    [Fact]
    public async Task Balance_Debit_While_Still_Pending_Records_Anomaly_Only()
    {
        var order = await PrepareWechatPaidCandidateAsync();

        // BalanceDebitedEto 到达时订单应已是 Paid；仍是 PendingPayment 只记异常不改状态
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleBalanceDebitedAsync(order.Id, Guid.NewGuid(), 50m, CollectionCurrency.Usdt));

        var current = await UowAsync(() => ReloadAsync(order.Id));
        current.Status.ShouldBe(OrderStatus.PendingPayment);
        current.PaymentAnomaly.ShouldBeTrue();
    }


    [Fact]
    public async Task Concurrent_Balance_Pay_Marks_Paid_Exactly_Once()
    {
        // 计划第 8 节第一条：同一订单并发支付只有一次成功。条件更新 WHERE Status =
        /// PendingPayment 在行锁上串行化两个并发扣款：先到者标 Paid，后到者 0 行回滚并
        ///按「已 Paid 且归因相同」当作成功——库里只有一次 Paid、一条扣款归因、零异常
        var order = await UowAsync(() => CreateBusinessOrderAsync());
        var manager = GetRequiredService<OrderPaymentManager>();
        var quote = await UowAsync(() => GetRequiredService<Quotes.QuoteManager>()
            .GetOrCreateActiveQuoteAsync(order, OrderingPayChannel.BalanceUsdt));

        // 各自开独立环境 UoW（等价两个并发请求；共享环境会把同一个 DbContext 暴露给两线程）：
        // 内层 requiresNew 各自原子，后到者回滚后的状态读取落在各自的环境 UoW 上
        var results = await Task.WhenAll(
            UowAsync(() => manager.PayWithBalanceAsync(order, quote)),
            UowAsync(() => manager.PayWithBalanceAsync(order, quote)));

        results.All(r => r.Outcome == StartPayOutcome.BalancePaid).ShouldBeTrue(
            "后到者按归因相同当作成功（计划第 3.2 节），两个调用都不该失败");

        var current = await UowAsync(() => ReloadAsync(order.Id));
        current.Status.ShouldBe(OrderStatus.Paid);
        current.PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdt);
        current.BalanceTxnId.ShouldNotBeNull();
        (await UowAsync(() => AnomalyLinesAsync(order.Id))).Count.ShouldBe(0); // 无归因冲突，无异常
    }

    [Fact]
    public async Task Balance_Paid_Order_Late_Channel_Completion_Records_Anomaly()
    {
        // 评审 H 级回归：余额支付后的订单残留旧 CollectionId（换渠道前的渠道尝试），
        // 旧渠道实际付款完成时不得被「同一来源再投递」吞掉——必须记异常
        var order = await PrepareWechatPaidCandidateAsync();
        var current = await UowAsync(() => ReloadAsync(order.Id));
        var pay = current.PayAmount!.Value;
        var manager = GetRequiredService<OrderPaymentManager>();

        // 先走余额支付（旧收款被桩取消；订单 Paid，BalanceTxnId 非空，CollectionId 残留旧值）
        var balanceQuote = await UowAsync(() => GetRequiredService<Quotes.QuoteManager>()
            .GetOrCreateActiveQuoteAsync(current, OrderingPayChannel.BalanceUsdt));
        await UowAsync(() => manager.PayWithBalanceAsync(current, balanceQuote));

        // 旧渠道收款随后完成（金额与订单应付一致）
        await UowAsync(() => manager.HandleCollectionCompletedAsync(
            order.Id, current.CollectionId!.Value, OrderingPayChannel.WechatNative,
            pay, CollectionCurrency.Cny, "wx-late"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Paid);
        after.PaymentAnomaly.ShouldBeTrue(); // 不是同一来源：记异常，不吞
        (await UowAsync(() => AnomalyLinesAsync(order.Id))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Event_Handler_Ignores_Non_Order_BusinessType()
    {
        var order = await PrepareWechatPaidCandidateAsync();
        var handler = GetRequiredService<Handlers.CollectionCompletedEventHandler>();

        await UowAsync(() => handler.HandleEventAsync(new CollectionCompletedEto
        {
            CollectionId = Guid.NewGuid(),
            CollectionNo = "STUB-OTHER",
            BusinessType = CollectionBusinessTypes.TopUp, // 非订单业务：必须忽略
            BusinessId = order.Id,
            UserId = UserId,
            Channel = CollectionChannel.WechatNative,
            Currency = CollectionCurrency.Cny,
            Amount = "1.00",
            CompletedAt = DateTime.UtcNow,
        }));

        (await UowAsync(() => ReloadAsync(order.Id))).Status.ShouldBe(OrderStatus.PendingPayment);
        (await UowAsync(() => AnomalyLinesAsync(order.Id))).Count.ShouldBe(0);
    }

    // ── 共用步骤 ─────────────────────────────────────────────────────────────────────────

    private async Task<OrdOrder> CreateBusinessOrderAsync(decimal amount = 10m)
    {
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var order = new OrdOrder(Guid.NewGuid())
        {
            OrderNo = "ORD-" + Guid.NewGuid().ToString("N")[..10],
            UserId = UserId,
            OrderType = OrderType.Business,
            Subject = "state-machine-test",
            Amount = amount,
            Currency = "USD",
            Status = OrderStatus.PendingPayment,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.Orders.AddAsync(order);
        await dbContext.SaveChangesAsync();
        return order;
    }

    /// <summary>创建业务单并发起微信支付（未完成）。</summary>
    private async Task<OrdOrder> PrepareWechatPaidCandidateAsync()
    {
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        return await UowAsync(async () =>
        {
            var order = await CreateBusinessOrderAsync();
            var quote = await quoteManager.GetOrCreateActiveQuoteAsync(order, OrderingPayChannel.WechatNative);
            await manager.StartChannelPayAsync(order, quote);
            return await ReloadAsync(order.Id);
        });
    }

    private async Task<OrdOrder> MarkPaidViaWechatAsync()
    {
        var candidate = await PrepareWechatPaidCandidateAsync();
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(candidate.Id, candidate.CollectionId!.Value,
                OrderingPayChannel.WechatNative, candidate.PayAmount!.Value, CollectionCurrency.Cny, "wx-trade-1"));
        return candidate;
    }

    private async Task<OrdOrder> ReloadAsync(Guid orderId)
    {
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        return await dbContext.Orders.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(o => o.Id == orderId);
    }

    private async Task<System.Collections.Generic.List<OrdPaymentAnomalyLine>> AnomalyLinesAsync(Guid orderId)
    {
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        return await dbContext.PaymentAnomalyLines.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .ToListAsync();
    }
}
