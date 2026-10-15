using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using AbpAdmin.Ordering.Quotes;
using AbpAdmin.Ordering.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// QuoteManager 接线用例（真实模块 + 设置存储）：X_min 的 CNY/USD 比较基准（计划第 4.2/8 节：
/// 不能拿 CNY 下限直接和 USD 金额比）、Stripe 有效期 30 分钟下限、报价有效期 15 分钟下限、
/// Active 报价过期后在同一事务里翻状态并立刻可重新报价（计划第 4.2 节）。
/// </summary>
public class QuoteManagerTests : OrderingTestBase
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Wechat_XMin_Compares_Against_Amount_Times_Rate()
    {
        // X_min = S / r = 0.3 / 0.006 = 50 CNY；订单 10 USD × 7.2 = 72 CNY ≥ 50 → 放行。
        // 若错误地拿 50（CNY 下限）直接和 10（USD 金额）比，这里会抛 BelowMinimumAmount——
        // 本用例就是钉住「必须乘汇率再比」这条基准
        await SetGlobalAsync(OrderingSettings.Quote.WechatSettlementFloor, "0.3");
        await SetGlobalAsync(OrderingSettings.Quote.WechatPercentRate, "0.006");

        var order = await CreateOrderAsync(10m);
        var quote = await QuoteForAsync(order, OrderingPayChannel.WechatNative);

        quote.PayCurrency.ShouldBe(AbpAdmin.Collection.Contracts.CollectionCurrency.Cny);
        quote.PayAmount.ShouldBe(72.44m); // ceil(72 × 1.006 + 0 默认固定费, 0.01)
    }

    [Fact]
    public async Task Wechat_XMin_Rejects_When_Cny_Comparable_Below_Min()
    {
        // X_min = 0.5 / 0.006 ≈ 83.33 CNY > 72 → 拒绝
        await SetGlobalAsync(OrderingSettings.Quote.WechatSettlementFloor, "0.5");
        await SetGlobalAsync(OrderingSettings.Quote.WechatPercentRate, "0.006");

        var order = await CreateOrderAsync(10m);
        (await Should.ThrowAsync<BusinessException>(
                () => QuoteForAsync(order, OrderingPayChannel.WechatNative)))
            .Code.ShouldBe(OrderingErrorCodes.BelowMinimumAmount);
    }

    [Fact]
    public async Task Stripe_Quote_Clamped_To_At_Least_30_Minutes()
    {
        await SetGlobalAsync(OrderingSettings.Quote.ValidityMinutes, "5"); // 全局有效期低于下限

        var order = await CreateOrderAsync(10m);
        var quote = await QuoteForAsync(order, OrderingPayChannel.StripeCheckout);

        (quote.ExpiresAt - quote.CreationTime).ShouldBeGreaterThanOrEqualTo(
            QuoteManager.StripeMinimumValidity.Subtract(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Quote_Validity_Floored_At_15_Minutes()
    {
        await SetGlobalAsync(OrderingSettings.Quote.ValidityMinutes, "5");

        var order = await CreateOrderAsync(10m);
        var quote = await QuoteForAsync(order, OrderingPayChannel.WechatNative);

        (quote.ExpiresAt - quote.CreationTime).ShouldBeGreaterThanOrEqualTo(
            TimeSpan.FromMinutes(OrderingSettings.Defaults.ValidityMinutes).Subtract(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Expired_Active_Quote_Is_Flipped_And_Requoted_Immediately()
    {
        var order = await CreateOrderAsync(10m);
        var first = await QuoteForAsync(order, OrderingPayChannel.WechatNative);

        // 把 Active 报价的有效期压到过去（作业还没跑的漏网形态）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            (await dbContext.Quotes.Where(q => q.Id == first.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))))
                .ShouldBe(1);
            await uow.CompleteAsync();
        }

        // 过期之后立刻可以重新报价，不必等作业（计划第 4.2 节）
        var second = await QuoteForAsync(order, OrderingPayChannel.WechatNative);

        second.Id.ShouldNotBe(first.Id);
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var oldRow = await dbContext.Quotes.AsNoTracking().SingleAsync(q => q.Id == first.Id);
            await uow.CompleteAsync();
            oldRow.Status.ShouldBe(QuoteStatus.Expired);
        }
    }

    [Fact]
    public async Task Concurrent_StartPay_Unique_Violation_Reads_Back_Winner_Idempotently()
    {
        // 本窗口修复的并发 start-pay 双击回归测试（OCR test 修复：catch 23505 → 幂等读回此前零覆盖）。
        // 确定性复现：事务 A 插入 Active 赢者但不提交 → 事务 B 走 QuoteManager，SELECT 读不到未提交行 →
        // INSERT 在部分唯一索引上阻塞 → A 提交 → B 撞 23505 进 catch → 读回赢者，双双 200。
        var order = await CreateOrderAsync(10m);
        var method = OrderingPayChannel.WechatNative;

        var scopeFactory = GetRequiredService<IServiceScopeFactory>();
        OrdQuote winner;
        OrdQuote loser;
        using (var scope1 = scopeFactory.CreateScope())
        {
            using var uow1 = scope1.ServiceProvider.GetRequiredService<IUnitOfWorkManager>()
                .Begin(new Volo.Abp.Uow.AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
            var db1 = await scope1.ServiceProvider.GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            winner = new OrdQuote(Guid.NewGuid())
            {
                OrderId = order.Id,
                PayMethod = method,
                PayCurrency = AbpAdmin.Collection.Contracts.CollectionCurrency.Cny,
                PayAmount = 72.44m,
                UsdCnyRate = 7.2m,
                Status = QuoteStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                CreationTime = DateTime.UtcNow,
            };
            await db1.Quotes.AddAsync(winner);
            await db1.SaveChangesAsync(); // 未提交：模拟并发赢者已插行、未到提交点

            Task<OrdQuote> loserTask;
            using (var scope2 = scopeFactory.CreateScope())
            {
                loserTask = Task.Run(async () =>
                {
                    using var uow2 = scope2.ServiceProvider.GetRequiredService<IUnitOfWorkManager>()
                        .Begin(new Volo.Abp.Uow.AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
                    var quote = await scope2.ServiceProvider.GetRequiredService<QuoteManager>()
                        .GetOrCreateActiveQuoteAsync(order, method);
                    await uow2.CompleteAsync();
                    return quote;
                });

                // 等 loser 走到 INSERT 阻塞点（唯一索引等 winner 的事务出结果），再提交赢者。
                // 若 loser 未达阻塞点就先提交，SELECT 直接读到已提交赢者走幂等读回——两条路径结果一致，
                // 但只有前者覆盖 23505 catch 分支（OCR R2 修复：await 移进 scope2，避免提前释放 loser 借用的作用域）
                await Task.Delay(500);
                await uow1.CompleteAsync();
                loser = await loserTask;
            }
            loser.Id.ShouldBe(winner.Id); // 读回的是赢者：500 变 200，且不是自己新插的行
        }

        // Active 行恰一条（赢者）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            var active = await dbContext.Quotes.AsNoTracking()
                .Where(q => q.OrderId == order.Id && q.PayMethod == method && q.Status == QuoteStatus.Active)
                .ToListAsync();
            await uow.CompleteAsync();
            active.Count.ShouldBe(1);
            active[0].Id.ShouldBe(winner.Id);
        }
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private async Task<OrdQuote> QuoteForAsync(OrdOrder order, OrderingPayChannel method)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var quote = await GetRequiredService<QuoteManager>().GetOrCreateActiveQuoteAsync(order, method);
        await uow.CompleteAsync();
        return quote;
    }

    private async Task<OrdOrder> CreateOrderAsync(decimal amount)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var order = new OrdOrder(Guid.NewGuid())
        {
            OrderNo = "ORD-QUOTE-" + Guid.NewGuid().ToString("N")[..8],
            UserId = UserId,
            OrderType = OrderType.Business,
            Subject = "quote-test",
            Amount = amount,
            Currency = "USD",
            Status = OrderStatus.PendingPayment,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.Orders.AddAsync(order);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return order;
    }

    private Task SetGlobalAsync(string name, string value)
    {
        return GetRequiredService<ISettingManager>().SetGlobalAsync(name, value);
    }
}
