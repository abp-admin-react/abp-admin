using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public enum PayMethod { Wechat, Alipay }

public enum OrderStatus { Pending, Paid, Expired, Failed }

public sealed class Order
{
    public required string OrderId { get; init; }
    public required Guid UserId { get; init; }
    public required string PlanId { get; init; }
    public required decimal AmountCny { get; init; }
    public required long Tokens { get; init; }
    public required PayMethod Method { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int QrVersion { get; set; } = 1;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTimeOffset? PaidAt { get; set; }
}

/// <summary>
/// 充值单状态机：Pending →(支付回调/模拟)→ Paid（入账一次）；Pending →(过期)→ Expired →(刷新二维码)→ Pending；
/// Pending →(渠道失败)→ Failed。Paid/Failed 为终态。真实商户回调走 MarkPaid 钩子。
/// </summary>
public class OrderBook(TimeProvider clock, PlanCatalog plans, Wallet wallet, IOptions<TokenGatewayOptions> options)
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();

    private TimeSpan Ttl => TimeSpan.FromSeconds(options.Value.Payment.OrderTtlSeconds);

    public Order Create(Guid user, string? planId, PayMethod method)
    {
        var plan = plans.Find(planId);
        var now = clock.GetUtcNow();
        var order = new Order
        {
            OrderId = "hbo" + now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyyMMddHHmmss") + Guid.NewGuid().ToString("N")[..8],
            UserId = user, PlanId = plan.Id, AmountCny = plan.PriceCny, Tokens = plan.Tokens, Method = method,
            CreatedAt = now, ExpiresAt = now + Ttl,
        };
        _orders[order.OrderId] = order;
        return order;
    }

    public Order Get(Guid user, string id)
    {
        if (!_orders.TryGetValue(id, out var o) || o.UserId != user) throw GatewayException.NotFound();
        lock (o)
        {
            if (o.Status == OrderStatus.Pending && clock.GetUtcNow() >= o.ExpiresAt) o.Status = OrderStatus.Expired;
            return o;
        }
    }

    public Order RefreshQr(Guid user, string id)
    {
        var o = Get(user, id);
        lock (o)
        {
            if (o.Status is OrderStatus.Paid or OrderStatus.Failed)
                throw GatewayException.BadRequest("订单已结束");
            o.Status = OrderStatus.Pending;
            o.ExpiresAt = clock.GetUtcNow() + Ttl;
            o.QrVersion++;
            return o;
        }
    }

    /// <summary>支付成功（真实回调或测试模拟）。幂等；已过期但渠道确认到账也入账。</summary>
    public Order MarkPaid(string id)
    {
        if (!_orders.TryGetValue(id, out var o)) throw GatewayException.NotFound();
        lock (o)
        {
            if (o.Status == OrderStatus.Paid) return o;
            if (o.Status == OrderStatus.Failed) throw GatewayException.BadRequest("订单已失败");
            wallet.Credit(o.UserId, o.Tokens, o.OrderId);
            o.Status = OrderStatus.Paid;
            o.PaidAt = clock.GetUtcNow();
            return o;
        }
    }

    public Order MarkFailed(string id)
    {
        if (!_orders.TryGetValue(id, out var o)) throw GatewayException.NotFound();
        lock (o)
        {
            if (o.Status == OrderStatus.Paid) throw GatewayException.BadRequest("订单已支付");
            o.Status = OrderStatus.Failed;
            return o;
        }
    }
}
