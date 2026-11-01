using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public enum PayMethod { Wechat = 1, Alipay = 2 }

public enum OrderStatus { Pending = 0, Paid = 1, Expired = 2, Failed = 3 }

/// <summary>
/// 充值单状态机：Pending →(回调/模拟)→ Paid（入账一次）；Pending →(过期)→ Expired →(刷新二维码)→ Pending；
/// Pending/Expired →(渠道失败)→ Failed。Paid/Failed 终态。状态翻转全用条件 UPDATE。
/// 订单金额/代币数在建单时从价表快照，后续改价不影响已建单。
/// </summary>
public class OrderBook(IDbContextFactory<TokenGatewayDbContext> dbf, TimeProvider clock, PlanCatalog plans, Wallet wallet, IOptions<TokenGatewayOptions> options)
{
    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
    private TimeSpan Ttl => TimeSpan.FromSeconds(options.Value.Payment.OrderTtlSeconds);

    public async Task<TgOrder> CreateAsync(Guid user, string? planId, PayMethod method)
    {
        var plan = await plans.FindActiveAsync(planId);
        var now = Now();
        var order = new TgOrder
        {
            OrderId = "hbo" + clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString("N")[..8],
            UserId = user, PlanId = plan.Id, AmountCny = plan.PriceCny, Tokens = plan.Tokens, Method = method,
            Status = OrderStatus.Pending, CreatedAt = now, ExpiresAt = now + Ttl,
        };
        await using var db = await dbf.CreateDbContextAsync();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<TgOrder> GetAsync(Guid user, string id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var now = Now();
        await db.Orders.Where(o => o.OrderId == id && o.Status == OrderStatus.Pending && o.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Expired));
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
        if (order is null || order.UserId != user) throw GatewayException.NotFound();
        return order;
    }

    public async Task<TgOrder> RefreshQrAsync(Guid user, string id)
    {
        await GetAsync(user, id);
        var exp = Now() + Ttl;
        await using var db = await dbf.CreateDbContextAsync();
        var n = await db.Orders.Where(o => o.OrderId == id && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Expired))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Pending).SetProperty(o => o.ExpiresAt, exp).SetProperty(o => o.QrVersion, o => o.QrVersion + 1));
        if (n == 0) throw GatewayException.BadRequest("订单已结束");
        return await GetAsync(user, id);
    }

    /// <summary>支付成功（真实回调或开发环境模拟）。幂等；已过期但渠道确认到账也入账。翻状态与入账同一事务。</summary>
    public async Task<TgOrder> MarkPaidAsync(string id)
    {
        var now = Now();
        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == id) ?? throw GatewayException.NotFound();
        if (o.Status == OrderStatus.Failed) throw GatewayException.BadRequest("订单已失败");
        var n = await db.Orders.Where(x => x.OrderId == id && (x.Status == OrderStatus.Pending || x.Status == OrderStatus.Expired))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OrderStatus.Paid).SetProperty(x => x.PaidAt, now));
        if (n == 1) await wallet.CreditInTxAsync(db, o.UserId, o.Tokens, o.OrderId);
        await tx.CommitAsync();
        return await db.Orders.AsNoTracking().FirstAsync(x => x.OrderId == id);
    }

    public async Task<TgOrder> MarkFailedAsync(string id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var n = await db.Orders.Where(x => x.OrderId == id && (x.Status == OrderStatus.Pending || x.Status == OrderStatus.Expired))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OrderStatus.Failed));
        var o = await db.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == id) ?? throw GatewayException.NotFound();
        if (n == 0 && o.Status != OrderStatus.Failed) throw GatewayException.BadRequest("订单已支付");
        return o;
    }
}
