using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Jobs;
using AbpAdmin.Ordering.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// 补状态作业用例（桩环境，计划第 3.3、8 节）：事件丢失时作业能发现已支付订单的第二笔到账；
/// 过期待支付单按 CancelAsync→关单路径处理；到账匹配把待支付单标 Paid；
/// 错额异常行只追加一次（PaymentAnomaly 标志去重，评审 M 级）。
/// </summary>
public class OrderStatusSyncJobTests : OrderingStubTestBase
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Job_Finds_Second_Completed_After_Paid()
    {
        var order = await PreparePaidOrderWithSecondCollectionAsync();

        await ExecuteJobAsync();

        var lines = await ReadAsync(async dbContext =>
            await dbContext.PaymentAnomalyLines.AsNoTracking().Where(l => l.OrderId == order.Id).ToListAsync());
        lines.Count.ShouldBe(1); // 第二笔到账：追加异常明细并告警
        lines[0].CollectionId.ShouldNotBeNull();
        var current = await ReloadAsync(order.Id);
        current.Status.ShouldBe(OrderStatus.Paid); // 状态不动，只记明细
        current.PaymentAnomaly.ShouldBeTrue();
    }

    [Fact]
    public async Task Job_Closes_Expired_Pending_Order_Without_Collection()
    {
        var orderId = await CreateOrderAsync(expired: true);

        await ExecuteJobAsync();

        (await ReloadAsync(orderId)).Status.ShouldBe(OrderStatus.Closed);
        (await ReloadAsync(orderId)).CloseReason.ShouldBe(OrderCloseReason.Expired);
    }

    [Fact]
    public async Task Job_Marks_Pending_Paid_From_Matching_Completed()
    {
        // 发起微信支付（PayAmount 已写入），桩里的收款随后变为 Completed
        var order = await StartWechatPayAsync();
        var dto = await Stub.GetAsync((await ReloadAsync(order.Id)).CollectionId!.Value);
        dto.ShouldNotBeNull();
        dto.Status = CollectionStatus.Completed;

        await ExecuteJobAsync();

        var current = await ReloadAsync(order.Id);
        current.Status.ShouldBe(OrderStatus.Paid);
        current.CollectionId.ShouldBe(dto.Id);
    }

    [Fact]
    public async Task Job_Appends_Mismatch_Anomaly_Only_Once()
    {
        var order = await StartWechatPayAsync(amount: 10m);
        var dto = await Stub.GetAsync((await ReloadAsync(order.Id)).CollectionId!.Value);
        dto.ShouldNotBeNull();
        dto.Status = CollectionStatus.Completed;
        dto.Amount = CollectionAmounts.Format(
            CollectionAmounts.Parse(dto.Amount) + 10m); // 错额完成

        await ExecuteJobAsync();
        await ExecuteJobAsync(); // 第二轮不得重复写行

        var lines = await ReadAsync(async dbContext =>
            await dbContext.PaymentAnomalyLines.AsNoTracking().Where(l => l.OrderId == order.Id).ToListAsync());
        lines.Count.ShouldBe(1);
        (await ReloadAsync(order.Id)).Status.ShouldBe(OrderStatus.PendingPayment); // 错额不提前关单
    }


    // ── M2：充值补插 / 已扣未标支付（计划第 3.3 节）────────────────────────────────────

    [Fact]
    public async Task Job_Backfills_TopUp_Orders_From_Credited_Txns()
    {
        var txnA = Guid.NewGuid();
        var txnB = Guid.NewGuid();
        Stub.CreditedTxns.Add(new CreditedTxnDto { Seq = 1, BalanceTxnId = txnA, UserId = UserId, Currency = CollectionCurrency.Usdt, Amount = "10.5", CreditedAt = DateTime.Now });
        Stub.CreditedTxns.Add(new CreditedTxnDto { Seq = 2, BalanceTxnId = txnB, UserId = UserId, Currency = CollectionCurrency.Usdc, Amount = "3", CreditedAt = DateTime.Now });

        await ExecuteJobAsync();

        var topUps = await ReadAsync(async dbContext =>
            await dbContext.Orders.AsNoTracking().Where(o => o.OrderType == OrderType.TopUp).ToListAsync());
        topUps.Count.ShouldBe(2); // 每笔已入账充值一条 Paid 充值订单，停在 Paid（计划第 2 节）
        topUps.ShouldAllBe(o => o.Status == OrderStatus.Paid);
        topUps.Single(o => o.BalanceTxnId == txnA).PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdt);
        topUps.Single(o => o.BalanceTxnId == txnA).PayNetwork.ShouldBe("BSC");   // 渠道模型：充值单带来源网络
        topUps.Single(o => o.BalanceTxnId == txnB).PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdc);
        topUps.Single(o => o.BalanceTxnId == txnB).PayNetwork.ShouldBe("BSC");

        // 幂等：重跑不重复插（余额流水重扫会再次带回，按充值订单存在性去重）
        await ExecuteJobAsync();
        (await ReadAsync(async dbContext =>
            await dbContext.Orders.AsNoTracking().CountAsync(o => o.OrderType == OrderType.TopUp))).ShouldBe(2);
    }

    [Fact]
    public async Task Job_Marks_Pending_Paid_From_Unreversed_Debit()
    {
        var orderId = await CreateOrderAsync();
        await SetPayableAsync(orderId, 12.42m, CollectionCurrency.Usdt);
        Stub.SetDebit(OrderingConsts.OrderBusinessType, orderId, new DebitDto
        {
            BalanceTxnId = Guid.NewGuid(),
            BusinessType = OrderingConsts.OrderBusinessType,
            BusinessId = orderId,
            UserId = UserId,
            Currency = CollectionCurrency.Usdt,
            Amount = "12.42",
        });

        await ExecuteJobAsync();

        var current = await ReloadAsync(orderId);
        current.Status.ShouldBe(OrderStatus.Paid);
        current.PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdt);
        current.PayNetwork.ShouldBe("BSC");   // 渠道模型：复活单带来源网络
        current.BalanceTxnId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Job_Revives_Expired_Closed_Order_From_Matching_Debit()
    {
        var orderId = await CreateOrderAsync();
        await SetPayableAsync(orderId, 40m, CollectionCurrency.Usdt);
        var txnId = Guid.NewGuid();
        Stub.SetDebit(OrderingConsts.OrderBusinessType, orderId, new DebitDto
        {
            BalanceTxnId = txnId, BusinessType = OrderingConsts.OrderBusinessType, BusinessId = orderId,
            UserId = UserId, Currency = CollectionCurrency.Usdt, Amount = "40",
        });

        // 推到过期：同一轮作业先关单（Expired），余额核对步骤随后按扣款复活
        await SetExpiredAsync(orderId);
        await ExecuteJobAsync();

        var current = await ReloadAsync(orderId);
        current.Status.ShouldBe(OrderStatus.Paid);
        current.BalanceTxnId.ShouldBe(txnId);
        current.PaymentAnomaly.ShouldBeFalse();
    }

    [Fact]
    public async Task Job_Reverses_Debit_And_Appends_Anomaly_On_UserCancelled()
    {
        var orderId = await CreateOrderAsync();
        await SetPayableAsync(orderId, 8m, CollectionCurrency.Usdc);
        var txnId = Guid.NewGuid();
        Stub.SetDebit(OrderingConsts.OrderBusinessType, orderId, new DebitDto
        {
            BalanceTxnId = txnId, BusinessType = OrderingConsts.OrderBusinessType, BusinessId = orderId,
            UserId = UserId, Currency = CollectionCurrency.Usdc, Amount = "8",
        });

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            await GetRequiredService<OrderPaymentManager>().CancelByUserAsync(await ReloadAsync(orderId));
            await uow.CompleteAsync();
        }
        await ExecuteJobAsync();

        var current = await ReloadAsync(orderId);
        current.Status.ShouldBe(OrderStatus.Closed); // 不复活
        current.CloseReason.ShouldBe(OrderCloseReason.UserCancel);
        Stub.ReversedBusinessIds.ShouldContain(orderId); // 扣款已回冲
        var lines = await ReadAsync(async dbContext =>
            await dbContext.PaymentAnomalyLines.AsNoTracking().Where(l => l.OrderId == orderId).ToListAsync());
        lines.Count.ShouldBe(1); // 异常明细留痕
    }


    [Fact]
    public async Task Job_Does_Not_Mark_Paid_When_Debit_Amount_Mismatches()
    {
        var orderId = await CreateOrderAsync();
        await SetPayableAsync(orderId, 12.42m, CollectionCurrency.Usdt);
        Stub.SetDebit(OrderingConsts.OrderBusinessType, orderId, new DebitDto
        {
            BalanceTxnId = Guid.NewGuid(), BusinessType = OrderingConsts.OrderBusinessType, BusinessId = orderId,
            UserId = UserId, Currency = CollectionCurrency.Usdt, Amount = "9", // 错额
        });

        await ExecuteJobAsync();

        var current = await ReloadAsync(orderId);
        current.Status.ShouldBe(OrderStatus.PendingPayment); // 错额扣款不是付款证据
        current.BalanceTxnId.ShouldBeNull();
    }

    [Fact]
    public async Task Job_Backfill_Advances_CreditCursor_And_Rescan_Rerun_Is_Idempotent()
    {
        Stub.CreditedTxns.Add(new CreditedTxnDto { Seq = 1, BalanceTxnId = Guid.NewGuid(), UserId = UserId, Currency = CollectionCurrency.Usdt, Amount = "1", CreditedAt = DateTime.Now });
        Stub.CreditedTxns.Add(new CreditedTxnDto { Seq = 2, BalanceTxnId = Guid.NewGuid(), UserId = UserId, Currency = CollectionCurrency.Usdt, Amount = "2", CreditedAt = DateTime.Now });

        await ExecuteJobAsync();

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var state = await dbContext.JobStates.AsNoTracking()
                .SingleAsync(j => j.Id == AbpAdmin.ScheduledJobs.ScheduledJobConsts.OrderStatusSyncJobType);
            state.CreditCursor.ShouldBe(2);
            state.CursorLastAdvancedAt.ShouldNotBeNull();
            await uow.CompleteAsync();
        }

        // 重跑：stub 的重扫腿会把窗口内的两行再次带回（契约语义），按充值订单存在性去重
        await ExecuteJobAsync();
        (await ReadAsync(async dbContext =>
            await dbContext.Orders.AsNoTracking().CountAsync(o => o.OrderType == OrderType.TopUp))).ShouldBe(2);
    }


    [Fact]
    public async Task Job_Skip_Counter_Alerts_And_Resets_At_Threshold()
    {
        // 坏金额行：作业里 Parse 抛 → 单行跳过路径（不需要注入缝，数据本身就是故障源）
        var bad = Guid.NewGuid();
        Stub.CreditedTxns.Add(new CreditedTxnDto { Seq = 1, BalanceTxnId = bad, UserId = UserId, Currency = CollectionCurrency.Usdt, Amount = "not-a-number", CreditedAt = DateTime.Now });

        // 阈值 2：第一轮计数 1，第二轮达阈值告警并清零重计
        await ExecuteJobAsync(payload: "{\"SkipAlertThreshold\":2}");
        (await ReadSkippedJsonAsync())!.ShouldContain($"\"{bad}\":1");

        await ExecuteJobAsync(payload: "{\"SkipAlertThreshold\":2}");
        (await ReadSkippedJsonAsync())!.ShouldContain($"\"{bad}\":0"); // 告警后重新计数

        // 坏金额行不产生充值订单
        (await ReadAsync(async dbContext =>
            await dbContext.Orders.AsNoTracking().CountAsync(o => o.OrderType == OrderType.TopUp && o.BalanceTxnId == bad))).ShouldBe(0);
    }

    private async Task SetPayableAsync(Guid orderId, decimal amount, CollectionCurrency currency)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        await dbContext.Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PayAmount, amount)
                .SetProperty(o => o.PayCurrency, currency));
        await uow.CompleteAsync();
    }

    private async Task SetExpiredAsync(Guid orderId)
    {
        var now = GetRequiredService<Volo.Abp.Timing.IClock>().Now;
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        await dbContext.Orders.Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, now.AddHours(-1)));
        await uow.CompleteAsync();
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private Task ExecuteJobAsync(string? payload = null)
    {
        // [ExposeServices(typeof(IScheduledJobHandler))] 只按接口暴露：按 JobType 从全部 handler 里挑
        var job = GetRequiredService<System.Collections.Generic.IEnumerable<AbpAdmin.ScheduledJobs.IScheduledJobHandler>>()
            .Single(h => h.JobType == AbpAdmin.ScheduledJobs.ScheduledJobConsts.OrderStatusSyncJobType);
        return job.ExecuteAsync(new AbpAdmin.ScheduledJobs.ScheduledJobContext
        {
            ScheduledJobId = Guid.NewGuid(),
            JobType = AbpAdmin.ScheduledJobs.ScheduledJobConsts.OrderStatusSyncJobType,
            Payload = payload,
        });
    }

    private async Task<string?> ReadSkippedJsonAsync()
    {
        return await ReadAsync(async dbContext =>
        {
            var state = await dbContext.JobStates.AsNoTracking()
                .SingleAsync(j => j.Id == AbpAdmin.ScheduledJobs.ScheduledJobConsts.OrderStatusSyncJobType);
            return state.SkippedTxnsJson;
        });
    }

    private async Task<Guid> CreateOrderAsync(bool expired = false, decimal amount = 10m)
    {
        // 过期与否由作业按 IClock（本地钟）判断，测试造数据必须同一口钟
        var now = GetRequiredService<Volo.Abp.Timing.IClock>().Now;
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var order = new OrdOrder(Guid.NewGuid())
        {
            OrderNo = "ORD-JOB-" + Guid.NewGuid().ToString("N")[..8],
            UserId = UserId,
            OrderType = OrderType.Business,
            Subject = "job-test",
            Amount = amount,
            Currency = "USD",
            Status = OrderStatus.PendingPayment,
            ExpiresAt = expired ? now.AddHours(-1) : now.AddHours(1),
            CreationTime = now,
        };
        await dbContext.Orders.AddAsync(order);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return order.Id;
    }

    private async Task<OrdOrder> StartWechatPayAsync(decimal amount = 10m)
    {
        var orderId = await CreateOrderAsync(amount: amount);
        var manager = GetRequiredService<OrderPaymentManager>();
        var quoteManager = GetRequiredService<Quotes.QuoteManager>();

        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var order = await dbContext.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        var quote = await quoteManager.GetOrCreateActiveQuoteAsync(order, OrderingPayChannel.WechatNative);
        await manager.StartChannelPayAsync(order, quote);
        await uow.CompleteAsync();
        return await ReloadAsync(orderId);
    }

    private async Task<OrdOrder> PreparePaidOrderWithSecondCollectionAsync()
    {
        var order = await StartWechatPayAsync();
        var manager = GetRequiredService<OrderPaymentManager>();

        // 当前收款完成 → 订单 Paid（事件路径）
        await RunInUowAsync(() => manager.HandleCollectionCompletedAsync(
            order.Id, order.CollectionId!.Value, OrderingPayChannel.WechatNative,
            order.PayAmount!.Value, CollectionCurrency.Cny, "wx-1"));

        // 第二笔（事件丢失，只能靠作业发现）
        var second = new CollectionDto
        {
            Id = Guid.NewGuid(),
            CollectionNo = "STUB-SECOND",
            BusinessType = OrderingConsts.OrderBusinessType,
            BusinessId = order.Id,
            UserId = UserId,
            Channel = CollectionChannel.WechatNative,
            Currency = CollectionCurrency.Cny,
            Amount = CollectionAmounts.Format(order.PayAmount!.Value),
            Status = CollectionStatus.Completed,
        };
        Stub.Track(second);
        return await ReloadAsync(order.Id);
    }

    private async Task<OrdOrder> ReloadAsync(Guid orderId)
    {
        return await ReadAsync(async dbContext =>
            await dbContext.Orders.AsNoTracking().IgnoreQueryFilters().FirstAsync(o => o.Id == orderId));
    }

    private async Task<T> ReadAsync<T>(Func<OrderingDbContext, Task<T>> read)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var result = await read(dbContext);
        await uow.CompleteAsync();
        return result;
    }

    private async Task RunInUowAsync(Func<Task> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        await action();
        await uow.CompleteAsync();
    }
}
