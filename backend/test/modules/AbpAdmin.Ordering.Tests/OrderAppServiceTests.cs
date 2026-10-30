using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Orders;
using AbpAdmin.Ordering.Quotes;
using AbpAdmin.Ordering.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// 用户端订单应用服务用例（桩环境）：钉住 Auto API 环境事务下的事务边界——
/// StartPayAsync 显式开 requiresNew 非事务工作单元，渠道异常不得连带回滚报价写库
///（报价随渠道异常被环境事务回滚的回归）；
/// Stripe 报价剩余不足 30 分钟时发起支付前强制换新报价。
/// </summary>
public class OrderAppServiceTests : OrderingStubTestBase
{
    [Fact]
    public async Task StartPayAsync_Quote_Survives_Channel_Failure()
    {
        var service = GetRequiredService<OrderAppService>();
        var orderId = await CreateBusinessOrderAsync(service);

        // 渠道明确失败（桩抛 ChannelNotConfigured 同族异常）
        Stub.CreateException = new BusinessException(CollectionErrorCodes.ChannelNotConfigured);
        await Should.ThrowAsync<BusinessException>(
            () => service.StartPayAsync(orderId, "WechatNative"));

        // 回归点：报价行必须留存（另一个工作单元能读到），订单仍是待支付
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var quotes = await dbContext.Quotes.AsNoTracking()
                .Where(q => q.OrderId == orderId && q.Status == QuoteStatus.Active)
                .ToListAsync();
            var orderRow = await dbContext.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            await uow.CompleteAsync();

            quotes.Count.ShouldBe(1, "渠道失败后报价被回滚——StartPayAsync 的非事务边界失效");
            orderRow.Status.ShouldBe(OrderStatus.PendingPayment);
        }
    }

    [Fact]
    public async Task StartPayAsync_Refreshes_Stripe_Quote_Below_30_Minutes()
    {
        var service = GetRequiredService<OrderAppService>();
        var orderId = await CreateBusinessOrderAsync(service);

        // 先生成一张 Stripe 报价，再把它的有效期压到「剩余 20 分钟」——未到 ExpiresAt、
        // 但 CreateAsync 会按剩余不足 30 分钟拒绝；不强制换新报价就会出现支付必败窗口
        await service.GetQuoteAsync(orderId, "StripeCheckout");
        await ShiftStripeQuoteExpiryAsync(orderId, minutesFromNow: 20);

        var result = await service.StartPayAsync(orderId, "StripeCheckout");

        result.Outcome.ShouldBe(StartPayOutcome.ChannelCreated.ToString());
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var quotes = await dbContext.Quotes.AsNoTracking()
                .Where(q => q.OrderId == orderId && q.PayMethod == OrderingPayChannel.StripeCheckout)
                .OrderBy(q => q.CreationTime)
                .ToListAsync();
            await uow.CompleteAsync();

            quotes.Count.ShouldBe(2);
            quotes[0].Status.ShouldBe(QuoteStatus.Expired);   // 旧报价被强制翻状态
            quotes[1].Status.ShouldBe(QuoteStatus.Active);    // 新报价接上
            var clock = GetRequiredService<Volo.Abp.Timing.IClock>();
            (quotes[1].ExpiresAt - clock.Now).ShouldBeGreaterThanOrEqualTo(
                QuoteManager.StripeMinimumValidity.Subtract(TimeSpan.FromMinutes(1)));
        }
    }

    [Fact]
    public async Task CreateAsync_Rejects_Control_Characters_In_Subject()
    {
        // subject 含 \u0000 穿过应用层校验后在 PG 写入时 22021 → 500；
        // 必须在应用层以业务错误拒绝（退款 Note 走同一校验助手）
        var service = GetRequiredService<OrderAppService>();

        await Should.ThrowAsync<BusinessException>(() => service.CreateAsync(new Services.Dtos.CreateOrderInput
        {
            Subject = "ab\u0000cd",
            Amount = "10.00",
        }));
    }


    [Fact]
    public async Task StartPayAsync_Balance_Pays_Through_App_Service()
    {
        var service = GetRequiredService<OrderAppService>();
        var orderId = await CreateBusinessOrderAsync(service);

        var result = await service.StartPayAsync(orderId, "BalanceUsdt");

        // 应用服务层的余额分支：桩记一次扣款、Outcome 映射、订单 Paid + 余额归因
        result.Outcome.ShouldBe(StartPayOutcome.BalancePaid.ToString());
        Stub.DebitCallCount.ShouldBe(1);
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var order = await dbContext.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            await uow.CompleteAsync();
            order.Status.ShouldBe(OrderStatus.Paid);
            order.PayChannel.ShouldBe(OrderingPayChannel.BalanceUsdt);
            order.BalanceTxnId.ShouldNotBeNull();
        }
    }


    [Fact]
    public async Task GetListAsync_Pages_Properly_And_Clamps_Page_Size()
    {
        var service = GetRequiredService<OrderAppService>();
        for (var i = 0; i < 3; i++)
        {
            await service.CreateAsync(new Services.Dtos.CreateOrderInput { Subject = $"pg-{i}", Amount = "1.00" });
        }

        // 第 1 页（页大小 2）：最新两笔
        var page1 = await service.GetListAsync(new Services.Dtos.OrderListFilterDto { SkipCount = 0, MaxResultCount = 2 });
        page1.TotalCount.ShouldBe(3);
        page1.Items.Count.ShouldBe(2);

        // 第 2 页：最旧一笔（硬顶 50 无 Skip 时永远取不到）
        var page2 = await service.GetListAsync(new Services.Dtos.OrderListFilterDto { SkipCount = 2, MaxResultCount = 2 });
        page2.Items.Count.ShouldBe(1);

        // 页大小超上限钳到 100；<=0 用缺省 20
        var clamped = await service.GetListAsync(new Services.Dtos.OrderListFilterDto { MaxResultCount = 9999 });
        clamped.Items.Count.ShouldBe(3);
        var negativeSkip = await service.GetListAsync(new Services.Dtos.OrderListFilterDto { SkipCount = -5, MaxResultCount = 2 });
        negativeSkip.Items.Count.ShouldBe(2);
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> CreateBusinessOrderAsync(OrderAppService service)
    {
        var created = await service.CreateAsync(new Services.Dtos.CreateOrderInput
        {
            Subject = "app-service-regression",
            Amount = "10.00",
        });
        return created.Id;
    }

    private async Task ShiftStripeQuoteExpiryAsync(Guid orderId, int minutesFromNow)
    {
        // 时间一律以服务的 IClock 为准（本地钟）：拿 UtcNow 写库会让报价在服务眼里已过期，
        // 测试就会测到别的分支
        var now = GetRequiredService<Volo.Abp.Timing.IClock>().Now;
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var rows = await dbContext.Quotes
            .Where(q => q.OrderId == orderId && q.PayMethod == OrderingPayChannel.StripeCheckout)
            .ExecuteUpdateAsync(s => s.SetProperty(
                q => q.ExpiresAt,
                now.AddMinutes(minutesFromNow)));
        await uow.CompleteAsync();
        rows.ShouldBe(1);
    }
}
