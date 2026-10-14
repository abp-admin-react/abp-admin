using System.Net;
using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Http;

public record CreateOrderInput(string? PlanId, string? Method, string? PayMethod);

[Route("api/wallet")]
public class WalletController(
    KeyStore keys, Wallet wallet, PlanCatalog plans, OrderBook orders, UsageLog usage,
    IOptions<TokenGatewayOptions> options, IWebHostEnvironment env, ILogger<WalletController> log) : GatewayControllerBase
{
    private Task<Guid> Me() => keys.ResolveAsync(Bearer(), allowSession: true);

    [HttpGet("balance")]
    public Task<IActionResult> Balance() => Run(async () =>
    {
        var v = await wallet.ViewAsync(await Me());
        return Ok(new
        {
            percent = v.Percent, low = v.Low, balance = v.Balance, frozen = v.Frozen, available = v.Available,
            periodUsed = v.PeriodUsed, updatedAt = v.UpdatedAt,
        });
    }, log);

    [HttpGet("plans")]
    public Task<IActionResult> Plans() => Run(async () =>
    {
        await Me();
        var items = await plans.AllAsync();
        return Ok(new { items = items.Select(p => new { id = p.Id, name = p.Name, priceCny = p.PriceCny, tokens = p.Tokens }) });
    }, log);

    [HttpPost("orders")]
    public Task<IActionResult> Create([FromBody] CreateOrderInput input) => Run(async () =>
    {
        var me = await Me();
        if (options.Value.Payment.Mode == PaymentMode.Live)
            throw new GatewayException(GatewayErrorCodes.UpstreamUnavailable, 503, "支付暂未开通");
        var method = (input.Method ?? input.PayMethod)?.ToLowerInvariant() switch
        {
            "wechat" => PayMethod.Wechat,
            "alipay" => PayMethod.Alipay,
            _ => throw GatewayException.BadRequest("method 只支持 wechat / alipay"),
        };
        return Ok(View(await orders.CreateAsync(me, input.PlanId, method)));
    }, log);

    [HttpGet("orders/{id}")]
    public Task<IActionResult> Get(string id) => Run(async () => Ok(View(await orders.GetAsync(await Me(), id))), log);

    [HttpPost("orders/{id}/refresh-qr")]
    public Task<IActionResult> RefreshQr(string id) => Run(async () => Ok(View(await orders.RefreshQrAsync(await Me(), id))), log);

    /// <summary>仅 Development 环境且支付为桩时可用；其他环境一律 404，与配置无关。</summary>
    [HttpPost("orders/{id}/simulate-paid")]
    public Task<IActionResult> SimulatePaid(string id) => Run(async () =>
    {
        if (!SimulatePayAllowed(env, options.Value)) throw GatewayException.NotFound();
        await orders.GetAsync(await Me(), id);
        return Ok(View(await orders.MarkPaidAsync(id)));
    }, log);

    internal static bool SimulatePayAllowed(IHostEnvironment env, TokenGatewayOptions o) =>
        env.IsDevelopment() && o.Payment.Mode == PaymentMode.Stub;

    /// <summary>桩二维码图（img 标签直接拉，无鉴权，不含敏感信息）。</summary>
    [HttpGet("orders/{id}/qr.svg")]
    public IActionResult QrStub(string id, string? m, string? a)
    {
        var label = WebUtility.HtmlEncode($"{(m == "alipay" ? "支付宝" : "微信")} ¥{a} · 联调桩");
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240">
            <rect width="240" height="240" fill="#fff" stroke="#999"/>
            <rect x="20" y="20" width="60" height="60" fill="#222"/><rect x="160" y="20" width="60" height="60" fill="#222"/>
            <rect x="20" y="160" width="60" height="60" fill="#222"/>
            <text x="120" y="125" font-size="14" text-anchor="middle" fill="#333">{label}</text>
            <text x="120" y="145" font-size="10" text-anchor="middle" fill="#888">{WebUtility.HtmlEncode(id)}</text>
            </svg>
            """;
        return Content(svg, "image/svg+xml");
    }

    // 真实商户回调钩子：商户就绪后在这里验签 → orders.MarkPaidAsync(outTradeNo)。
    [HttpPost("notify/wechat")]
    public IActionResult NotifyWechat() => StatusCode(501);

    [HttpPost("notify/alipay")]
    public IActionResult NotifyAlipay() => StatusCode(501);

    [HttpGet("usage")]
    public Task<IActionResult> Usage([FromQuery] int take = 50) => Run(async () =>
    {
        var me = await Me();
        var v = await wallet.ViewAsync(me);
        var items = (await usage.RecentAsync(me, Math.Clamp(take, 1, 200))).Select(e => new
        {
            time = e.Time, alias = e.Alias, bot = e.Bot, promptTokens = e.PromptTokens,
            completionTokens = e.CompletionTokens, cost = e.Cost, success = e.Success,
        });
        return Ok(new
        {
            summary = new { periodUsed = v.PeriodUsed, balance = v.Balance, frozen = v.Frozen, available = v.Available },
            items,
        });
    }, log);

    private object View(TgOrder o)
    {
        var method = o.Method == PayMethod.Wechat ? "wechat" : "alipay";
        var qr = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/wallet/orders/{o.OrderId}/qr.svg?m={method}&a={o.AmountCny}&v={o.QrVersion}";
        return new
        {
            orderId = o.OrderId, planId = o.PlanId, amountCny = o.AmountCny, tokens = o.Tokens, method,
            qrUrl = qr, expiresAt = new DateTimeOffset(o.ExpiresAt, TimeSpan.Zero), status = o.Status.ToString().ToLowerInvariant(),
            paidAt = o.PaidAt is { } p ? new DateTimeOffset(p, TimeSpan.Zero) : (DateTimeOffset?)null,
        };
    }
}
