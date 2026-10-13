using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using AbpAdmin.Ordering.Services;
using AbpAdmin.Ordering.Services.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Users;
using Volo.Abp;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// 订单的「商城可见性」字段（对比正常商城补齐的部分）：
/// 渠道单号随致付归因落库（用户对账凭证）、履约时间、人工退款累计与明细的用户侧可见、
/// 余额流水 → 订单 的关联反查。状态机迁移本身在 <see cref="OrderStateMachineTests"/>。
/// </summary>
public class OrderCommerceTests : OrderingTestBase
{
    /// <summary>测试宿主固定注入的当前用户（GetAsync/GetListAsync/GetOrderRefs 按 CurrentUser 过滤）。</summary>
    private Guid Me => GetRequiredService<ICurrentUser>().Id!.Value;

    private async Task<OrdOrder> CreateOrderAsync(Guid userId, decimal amount = 10m)
    {
        return await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var order = new OrdOrder(Guid.NewGuid())
            {
                OrderNo = "ORD-" + Guid.NewGuid().ToString("N")[..10],
                UserId = userId,
                OrderType = OrderType.Business,
                Subject = "commerce-test",
                Amount = amount,
                Currency = "USD",
                Status = OrderStatus.PendingPayment,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreationTime = DateTime.UtcNow,
            };
            await dbContext.Orders.AddAsync(order);
            await dbContext.SaveChangesAsync();
            return order;
        });
    }

    private async Task<T> UowAsync<T>(Func<Task<T>> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var result = await action();
        await uow.CompleteAsync();
        return result;
    }

    private async Task UowAsync(Func<Task> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        await action();
        await uow.CompleteAsync();
    }

    private async Task<OrdOrder> ReloadAsync(Guid orderId)
    {
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        return await dbContext.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
    }

    [Fact]
    public async Task Channel_Completion_Persists_TradeNo_On_Paid()
    {
        var order = await CreateOrderAsync(Me);
        var collectionId = Guid.NewGuid();
        // 到账核对的口径：应付三元组必须先就位（StartChannelPayAsync 的产物）
        await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            await dbContext.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PayAmount, 10m)
                    .SetProperty(o => o.PayCurrency, CollectionCurrency.Cny)
                    .SetProperty(o => o.CollectionId, collectionId)
                    .SetProperty(o => o.PayChannel, OrderingPayChannel.WechatNative));
        });

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, collectionId,
                OrderingPayChannel.WechatNative, 10m, CollectionCurrency.Cny, "wx-trade-commerce-1"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Paid);
        after.ChannelTradeNo.ShouldBe("wx-trade-commerce-1");
    }

    [Fact]
    public async Task Balance_Paid_Order_Has_No_TradeNo()
    {
        // 余额支付的单没有渠道单号（BalanceTxnId 归因），列保持 null
        var order = await CreateOrderAsync(Me);
        var balanceTxnId = Guid.NewGuid();

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .MarkPaidFromDebitAsync(order.Id, balanceTxnId, CollectionCurrency.Usdt));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Paid);
        after.BalanceTxnId.ShouldBe(balanceTxnId);
        after.ChannelTradeNo.ShouldBeNull();
    }

    [Fact]
    public async Task MarkFulfilled_Writes_FulfilledAt()
    {
        var order = await CreateOrderAsync(Me);
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .MarkPaidFromDebitAsync(order.Id, Guid.NewGuid(), CollectionCurrency.Usdt));

        await UowAsync(async () => await GetRequiredService<OrderPaymentManager>()
            .MarkFulfilledAsync(await ReloadAsync(order.Id)));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Fulfilled);
        after.FulfilledAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ManualRefund_Appends_UserVisible_Line_And_Accumulates()
    {
        var order = await CreateOrderAsync(Me, 20m);

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .AppendManualRefundAsync(order.Id, 5m, CollectionCurrency.Usdt, "first-part"));
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .AppendManualRefundAsync(order.Id, 3.5m, CollectionCurrency.Usdt, "second-part"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.RefundedAmount.ShouldBe(8.5m);
        // 退款是常规运营动作，不点亮 PaymentAnomaly 异常标（否则干净支付单永远挂红标，
        // 且过期复活守卫 !PaymentAnomaly 的语义被污染——OCR 评审 design F9）
        after.PaymentAnomaly.ShouldBeFalse();

        // 用户侧自己的详情能看到退款明细（可见性的验收点）
        var dto = await UowAsync(async () =>
        {
            var service = GetRequiredService<OrderAppService>();
            return await service.GetAsync(order.Id);
        });
        dto.RefundedAmount.ShouldBe("8.5");
        dto.AnomalyLines.ShouldNotBeNull();
        dto.AnomalyLines.Count(l => l.Note.Contains("first-part")).ShouldBe(1);
        dto.AnomalyLines.Count(l => l.Note.Contains("second-part")).ShouldBe(1);
    }

    [Fact]
    public async Task GetOrderRefs_Maps_Own_Txns_Only()
    {
        var orderA = await CreateOrderAsync(Me);
        var txnA = Guid.NewGuid();
        var orderB = await CreateOrderAsync(Guid.NewGuid()); // 他人订单
        var txnB = Guid.NewGuid();
        await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            await dbContext.Orders.Where(o => o.Id == orderA.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.BalanceTxnId, txnA));
            await dbContext.Orders.Where(o => o.Id == orderB.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.BalanceTxnId, txnB));
        });

        var result = await UowAsync(async () =>
        {
            var service = GetRequiredService<OrderAppService>();
            return await service.GetOrderRefsByBalanceTxnAsync(new List<Guid> { txnA, txnB, Guid.NewGuid() });
        });

        // 只映射当前用户的订单；他人订单的 txn 必须不出现（跨用户订单号零泄露）
        result.Count.ShouldBe(1);
        result.Single().BalanceTxnId.ShouldBe(txnA);
        result.Single().OrderId.ShouldBe(orderA.Id);
    }

    [Fact]
    public async Task Expired_Close_Revive_Persists_TradeNo()
    {
        // OCR test F2：复活也是渠道版标 Paid，对账凭证不能缺（修复前此路径丢 ChannelTradeNo）
        var order = await CreateOrderAsync(Me);
        var collectionId = Guid.NewGuid();
        await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            await dbContext.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PayAmount, 10m)
                    .SetProperty(o => o.PayCurrency, CollectionCurrency.Cny)
                    .SetProperty(o => o.CollectionId, collectionId)
                    .SetProperty(o => o.PayChannel, OrderingPayChannel.WechatNative)
                    .SetProperty(o => o.Status, OrderStatus.Closed)
                    .SetProperty(o => o.ClosedAt, DateTime.UtcNow)
                    .SetProperty(o => o.CloseReason, OrderCloseReason.Expired));
        });

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, collectionId,
                OrderingPayChannel.WechatNative, 10m, CollectionCurrency.Cny, "wx-trade-revive"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.Status.ShouldBe(OrderStatus.Paid);
        after.ChannelTradeNo.ShouldBe("wx-trade-revive");
    }

    [Fact]
    public async Task ManualRefund_Note_Only_Does_Not_Accumulate()
    {
        // OCR test F1：只记说明不记金额的分支（amount=null → 不累加、不抛）
        var order = await CreateOrderAsync(Me, 20m);
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .AppendManualRefundAsync(order.Id, 5m, CollectionCurrency.Usdt, "real-refund"));
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .AppendManualRefundAsync(order.Id, null, null, "note-only"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.RefundedAmount.ShouldBe(5m);           // null-amount 不动累计
        after.PaymentAnomaly.ShouldBeFalse();

        var dto = await UowAsync(async () => await GetRequiredService<OrderAppService>().GetAsync(order.Id));
        dto.AnomalyLines!.Count.ShouldBe(2);          // 两条明细都在，用户可见
    }

    [Fact]
    public async Task ManualRefund_Currency_Must_Match_Order_And_Max_Two_Decimals()
    {
        // OCR high-1 + M3：币种混入累计列 = 对用户的错误金额；子分位被 PG 静默舍入
        var order = await CreateOrderAsync(Me); // Currency = USD
        var service = GetRequiredService<OrderAdminAppService>();

        await Should.ThrowAsync<BusinessException>(async () =>
            await service.RecordManualRefundAsync(order.Id,
                new RecordManualRefundInput { Amount = "5.00", Currency = "Cny", Note = "currency mismatch" }));

        await Should.ThrowAsync<BusinessException>(async () =>
            await service.RecordManualRefundAsync(order.Id,
                new RecordManualRefundInput { Amount = "0.005", Currency = "USD", Note = "sub-cent" }));

        // 合法路径（币种匹配、两位小数）正常累加
        await service.RecordManualRefundAsync(order.Id,
            new RecordManualRefundInput { Amount = "2.25", Currency = "USD", Note = "ok" });
        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.RefundedAmount.ShouldBe(2.25m);
    }

    [Fact]
    public async Task Balance_Paid_And_TopUp_Orders_Carry_Bsc_Network()
    {
        // 渠道模型：Web3/余额类订单写 PayNetwork（当前单链 BSC），法币渠道单为 null
        var order = await CreateOrderAsync(Me);
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .MarkPaidFromDebitAsync(order.Id, Guid.NewGuid(), CollectionCurrency.Usdt));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdt);
        after.PayNetwork.ShouldBe("BSC");
    }

    [Fact]
    public async Task Channel_Paid_Order_Has_No_Network()
    {
        var order = await CreateOrderAsync(Me);
        var collectionId = Guid.NewGuid();
        await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            await dbContext.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PayAmount, 10m)
                    .SetProperty(o => o.PayCurrency, CollectionCurrency.Cny)
                    .SetProperty(o => o.CollectionId, collectionId)
                    .SetProperty(o => o.PayChannel, OrderingPayChannel.WechatNative));
        });

        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .HandleCollectionCompletedAsync(order.Id, collectionId,
                OrderingPayChannel.WechatNative, 10m, CollectionCurrency.Cny, "wx-net-probe"));

        var after = await UowAsync(() => ReloadAsync(order.Id));
        after.PayNetwork.ShouldBeNull();   // 法币渠道无网络概念
    }

    [Fact]
    public async Task GetOrderRefs_Empty_Input_Returns_Empty()
    {
        var service = GetRequiredService<OrderAppService>();
        var result = await UowAsync(async () => await service.GetOrderRefsByBalanceTxnAsync(new List<Guid>()));
        result.Count.ShouldBe(0);
    }

    [Fact]
    public async Task User_List_Includes_Anomaly_Lines()
    {
        var order = await CreateOrderAsync(Me);
        await UowAsync(() => GetRequiredService<OrderPaymentManager>()
            .AppendAnomalyAsync(order.Id, null, null, Guid.NewGuid(), "ch-trade-x",
                "A second completed collection arrived after the order was paid."));

        var list = await UowAsync(async () =>
        {
            var service = GetRequiredService<OrderAppService>();
            return await service.GetListAsync(new OrderListFilterDto());
        });

        var row = list.Items.Single(o => o.Id == order.Id);
        row.AnomalyLines.ShouldNotBeNull();
        row.AnomalyLines.Single().ChannelTradeNo.ShouldBe("ch-trade-x");
    }
}
