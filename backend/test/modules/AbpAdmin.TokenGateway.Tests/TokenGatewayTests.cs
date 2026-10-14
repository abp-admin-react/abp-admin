using System.Text.Json.Nodes;
using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.TokenGateway;

public class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public class TokenGatewayTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly IOptions<TokenGatewayOptions> _opt = Options.Create(new TokenGatewayOptions());
    private readonly Guid _u = Guid.NewGuid();

    private Wallet NewWallet() => new(_clock);

    private (OrderBook Orders, Wallet Wallet) NewOrders()
    {
        var w = NewWallet();
        return (new OrderBook(_clock, new PlanCatalog(_opt), w, _opt), w);
    }

    [Fact]
    public void Plans_are_four_tiers()
    {
        var plans = new PlanCatalog(_opt).All();
        plans.Select(p => p.PriceCny).ShouldBe(new[] { 30m, 98m, 198m, 498m });
        plans.Select(p => p.Name).ShouldBe(new[] { "体验包", "标准包", "进阶包", "专业包" });
        plans.ShouldAllBe(p => p.Tokens > 0);
    }

    [Fact]
    public void Percent_is_zero_and_low_when_never_credited()
    {
        var v = NewWallet().View(_u);
        v.Percent.ShouldBe(0);
        v.Low.ShouldBeTrue();
    }

    [Fact]
    public void Percent_floors_available_over_total_credited()
    {
        var w = NewWallet();
        w.Credit(_u, 1000, "o1");
        w.Credit(_u, 2000, "o2");
        var r = w.Reserve(_u, 500);
        w.Settle(_u, r, 299); // balance 2701
        w.Reserve(_u, 100);   // frozen 100 → available 2601
        var v = w.View(_u);
        v.Percent.ShouldBe(86); // floor(2601/3000*100)=86.7→86
        v.Low.ShouldBeFalse();
        v.Frozen.ShouldBe(100);
        v.PeriodUsed.ShouldBe(299);
    }

    [Fact]
    public void Release_does_not_change_denominator_and_low_under_10()
    {
        var w = NewWallet();
        w.Credit(_u, 1000, "o1");
        w.Settle(_u, w.Reserve(_u, 950), 950);
        w.Release(_u, w.Reserve(_u, 10));
        var v = w.View(_u);
        v.Percent.ShouldBe(5);
        v.Low.ShouldBeTrue();
        v.TotalCredited.ShouldBe(1000);
    }

    [Fact]
    public void Credit_is_idempotent_by_ref()
    {
        var w = NewWallet();
        w.Credit(_u, 100, "o1").ShouldBeTrue();
        w.Credit(_u, 100, "o1").ShouldBeFalse();
        w.View(_u).Balance.ShouldBe(100);
    }

    [Fact]
    public void Reserve_rejects_insufficient_balance()
    {
        var w = NewWallet();
        Should.Throw<GatewayException>(() => w.Reserve(_u, 1)).Code.ShouldBe("INSUFFICIENT_BALANCE");
        w.Credit(_u, 100, "o1");
        w.Reserve(_u, 80);
        Should.Throw<GatewayException>(() => w.Reserve(_u, 21)).Status.ShouldBe(402);
    }

    [Fact]
    public async Task Chat_with_no_balance_never_reaches_upstream()
    {
        var w = NewWallet();
        var billing = new ChatBilling(w, new UsageLog(), _opt);
        var req = JsonNode.Parse("""{"messages":[{"role":"user","content":"hi"}]}""")!.AsObject();
        Should.Throw<GatewayException>(() => billing.Begin(_u, req, null)).Code.ShouldBe("INSUFFICIENT_BALANCE");

        w.Credit(_u, 100_000, "o1");
        var t = billing.Begin(_u, req, "bot1");
        t.UpstreamBody["model"]!.GetValue<string>().ShouldBe("CHANGE_ME");
        var resp = await new MockUpstream().SendAsync(t.UpstreamBody, default);
        resp.IsSuccessStatusCode.ShouldBeTrue();
        billing.Settle(t, 12, 8).ShouldBe(20);
        var v = w.View(_u);
        v.Frozen.ShouldBe(0);
        v.Balance.ShouldBe(100_000 - 20);
    }

    [Fact]
    public void Failed_call_fully_unfreezes()
    {
        var w = NewWallet();
        w.Credit(_u, 100_000, "o1");
        var billing = new ChatBilling(w, new UsageLog(), _opt);
        var t = billing.Begin(_u, JsonNode.Parse("""{"messages":[]}""")!.AsObject(), null);
        w.View(_u).Frozen.ShouldBeGreaterThan(0);
        billing.Fail(t);
        w.View(_u).Balance.ShouldBe(100_000);
        w.View(_u).Frozen.ShouldBe(0);
    }

    [Fact]
    public void Invalid_or_missing_key_is_rejected()
    {
        var keys = new KeyStore(_clock, _opt);
        Should.Throw<GatewayException>(() => keys.Resolve(null, true)).Code.ShouldBe("INVALID_KEY");
        Should.Throw<GatewayException>(() => keys.Resolve("hb-nope", true)).Status.ShouldBe(401);
        var key = keys.IssueOrReuseKey(_u);
        key.ShouldStartWith("hb-");
        keys.IssueOrReuseKey(_u).ShouldBe(key);
        keys.Resolve(key, false).ShouldBe(_u);
        var (session, _) = keys.IssueSession(_u);
        Should.Throw<GatewayException>(() => keys.Resolve(session, allowSession: false));
        keys.Resolve(session, true).ShouldBe(_u);
        _clock.Now = _clock.Now.AddHours(_opt.Value.SessionHours + 1);
        Should.Throw<GatewayException>(() => keys.Resolve(session, true));
        keys.RevokeKey(_u);
        Should.Throw<GatewayException>(() => keys.Resolve(key, false));
    }

    [Fact]
    public void Order_pending_to_paid_credits_once()
    {
        var (orders, w) = NewOrders();
        var o = orders.Create(_u, "p98", PayMethod.Wechat);
        o.Status.ShouldBe(OrderStatus.Pending);
        o.AmountCny.ShouldBe(98m);
        orders.MarkPaid(o.OrderId);
        orders.MarkPaid(o.OrderId);
        orders.Get(_u, o.OrderId).Status.ShouldBe(OrderStatus.Paid);
        w.View(_u).Balance.ShouldBe(9_800_000);
        w.View(_u).Percent.ShouldBe(100);
        Should.Throw<GatewayException>(() => orders.RefreshQr(_u, o.OrderId));
    }

    [Fact]
    public void Order_expires_then_refresh_returns_pending()
    {
        var (orders, _) = NewOrders();
        var o = orders.Create(_u, "p30", PayMethod.Alipay);
        _clock.Now = _clock.Now.AddSeconds(301);
        orders.Get(_u, o.OrderId).Status.ShouldBe(OrderStatus.Expired);
        var r = orders.RefreshQr(_u, o.OrderId);
        r.Status.ShouldBe(OrderStatus.Pending);
        r.QrVersion.ShouldBe(2);
        r.ExpiresAt.ShouldBeGreaterThan(_clock.Now);
    }

    [Fact]
    public void Order_failed_is_terminal_and_owner_scoped()
    {
        var (orders, w) = NewOrders();
        var o = orders.Create(_u, "p30", PayMethod.Wechat);
        Should.Throw<GatewayException>(() => orders.Get(Guid.NewGuid(), o.OrderId)).Code.ShouldBe("NOT_FOUND");
        orders.MarkFailed(o.OrderId);
        Should.Throw<GatewayException>(() => orders.MarkPaid(o.OrderId));
        w.View(_u).Balance.ShouldBe(0);
        Should.Throw<GatewayException>(() => orders.Create(_u, "nope", PayMethod.Wechat)).Code.ShouldBe("BAD_REQUEST");
    }

    [Fact]
    public void Scrub_hides_upstream_traces()
    {
        var o = JsonNode.Parse("""{"id":"chatcmpl-x","model":"vendor/real","system_fingerprint":"fp","provider":"p"}""")!.AsObject();
        ChatController.Scrub(o, "hb-chat");
        o["model"]!.GetValue<string>().ShouldBe("hb-chat");
        o.ContainsKey("system_fingerprint").ShouldBeFalse();
        o.ContainsKey("provider").ShouldBeFalse();
        o["id"]!.GetValue<string>().ShouldStartWith("hb-");
    }
}
