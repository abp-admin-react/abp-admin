using System.Text.Json.Nodes;
using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Data;
using AbpAdmin.TokenGateway.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Shouldly;
using Xunit;

namespace AbpAdmin.TokenGateway;

[Collection(PgCollection.Name)]
public class TokenGatewayTests(PgFixture pg) : GatewayTestBase(pg)
{
    [Fact]
    public async Task Plans_seed_is_four_tiers_at_100k_per_yuan()
    {
        var plans = await NewPlans().AllAsync();
        plans.Select(p => p.PriceCny).ShouldBe(new[] { 30m, 98m, 198m, 498m });
        plans.Select(p => p.Name).ShouldBe(new[] { "体验包", "标准包", "进阶包", "专业包" });
        plans.ShouldAllBe(p => p.Tokens == (long)(p.PriceCny * 100_000));
    }

    [Fact]
    public async Task Plan_tokens_editable_and_new_orders_use_new_value_old_orders_keep_snapshot()
    {
        var catalog = NewPlans();
        var (orders, w) = NewOrders();
        var before = await orders.CreateAsync(U, "p198", PayMethod.Wechat);
        try
        {
            (await catalog.UpdateAsync("p198", new PlanPatch(null, null, 25_000_000, null, null))).Tokens.ShouldBe(25_000_000);
            (await orders.CreateAsync(U, "p198", PayMethod.Wechat)).Tokens.ShouldBe(25_000_000);
            await orders.MarkPaidAsync(before.OrderId);
            (await w.ViewAsync(U)).TotalCredited.ShouldBe(19_800_000);
            await Should.ThrowAsync<GatewayException>(() => catalog.UpdateAsync("p198", new PlanPatch(null, null, 0, null, null)));
        }
        finally
        {
            await catalog.UpdateAsync("p198", new PlanPatch(null, null, 19_800_000, null, null));
        }
    }

    [Fact]
    public async Task Percent_is_zero_and_low_when_never_credited()
    {
        var v = await NewWallet().ViewAsync(U);
        v.Percent.ShouldBe(0);
        v.Low.ShouldBeTrue();
    }

    [Fact]
    public async Task Percent_floors_available_over_total_credited()
    {
        var w = NewWallet();
        await w.CreditAsync(U, 1000, "t-" + Guid.NewGuid());
        await w.CreditAsync(U, 2000, "t-" + Guid.NewGuid());
        var r = await w.ReserveAsync(U, 500, "hb-chat");
        (await w.SettleAsync(U, r, 299)).ShouldBe(299);
        await w.ReserveAsync(U, 100, "hb-chat");
        var v = await w.ViewAsync(U);
        v.Percent.ShouldBe(86);  // floor(2601/3000*100)
        v.Low.ShouldBeFalse();
        v.Frozen.ShouldBe(100);
        v.Available.ShouldBe(3000 - 299 - 100);
        v.PeriodUsed.ShouldBe(299);
        (await w.ReconcileAsync(U)).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Release_keeps_denominator_and_low_under_10()
    {
        var w = NewWallet();
        await w.CreditAsync(U, 1000, "t-" + Guid.NewGuid());
        await w.SettleAsync(U, await w.ReserveAsync(U, 950, "a"), 950);
        await w.ReleaseAsync(U, await w.ReserveAsync(U, 10, "a"));
        var v = await w.ViewAsync(U);
        v.Percent.ShouldBe(5);
        v.Low.ShouldBeTrue();
        v.TotalCredited.ShouldBe(1000);
        (await w.ReconcileAsync(U)).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Credit_is_idempotent_by_ref()
    {
        var w = NewWallet();
        var @ref = "o-" + Guid.NewGuid();
        (await w.CreditAsync(U, 100, @ref)).ShouldBeTrue();
        (await w.CreditAsync(U, 100, @ref)).ShouldBeFalse();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => w.CreditAsync(U, 100, "c-" + U)));
        results.Count(x => x).ShouldBe(1);
        (await w.ViewAsync(U)).Balance.ShouldBe(200);
        (await w.ReconcileAsync(U)).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Reserve_rejects_insufficient_balance()
    {
        var w = NewWallet();
        (await Should.ThrowAsync<GatewayException>(() => w.ReserveAsync(U, 1, "a"))).Code.ShouldBe("INSUFFICIENT_BALANCE");
        await w.CreditAsync(U, 100, "t-" + Guid.NewGuid());
        await w.ReserveAsync(U, 80, "a");
        (await Should.ThrowAsync<GatewayException>(() => w.ReserveAsync(U, 21, "a"))).Status.ShouldBe(402);
    }

    [Fact]
    public async Task Concurrent_reserves_never_overdraw()
    {
        var w = NewWallet();
        await w.CreditAsync(U, 100, "t-" + Guid.NewGuid());
        var tasks = Enumerable.Range(0, 30).Select(async _ =>
        {
            try { await w.ReserveAsync(U, 10, "a"); return true; }
            catch (GatewayException) { return false; }
        });
        (await Task.WhenAll(tasks)).Count(x => x).ShouldBe(10);
        var v = await w.ViewAsync(U);
        v.Available.ShouldBe(0);
        v.Frozen.ShouldBe(100);
        (await w.ReconcileAsync(U)).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Settle_twice_charges_once_and_writes_ledger()
    {
        var w = NewWallet();
        await w.CreditAsync(U, 1000, "t-" + Guid.NewGuid());
        var r = await w.ReserveAsync(U, 300, "a");
        var both = await Task.WhenAll(w.SettleAsync(U, r, 120), w.SettleAsync(U, r, 120));
        both.Sum().ShouldBe(120);
        await w.ReleaseAsync(U, r);
        (await w.ViewAsync(U)).Balance.ShouldBe(880);

        await using var db = await Db.CreateDbContextAsync();
        var kinds = await db.Ledger.Where(e => e.UserId == U).OrderBy(e => e.Kind).Select(e => new { e.Kind, e.Amount }).ToListAsync();
        kinds.Select(k => (k.Kind, k.Amount)).ShouldBe(new[]
        {
            (LedgerKind.Credit, 1000L), (LedgerKind.Reserve, 300L), (LedgerKind.Settle, 120L), (LedgerKind.Release, 180L),
        });
        (await w.ReconcileAsync(U)).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Settle_clamps_to_held_and_zero_settles_to_full_release()
    {
        // 钳制分支补测（OCR test 修复）：流式用量超预占是真实形态——结算封顶到预占额；
        // actual=0 → 全额解冻、只写 Release 流水不写 Settle
        var w = NewWallet();
        await w.CreditAsync(U, 1000, "t-" + Guid.NewGuid());

        var r1 = await w.ReserveAsync(U, 300, "clamp-a");
        (await w.SettleAsync(U, r1, 10_000)).ShouldBe(300);
        var v1 = await w.ViewAsync(U);
        v1.Frozen.ShouldBe(0);
        v1.Balance.ShouldBe(700);

        var r2 = await w.ReserveAsync(U, 50, "clamp-b");
        (await w.SettleAsync(U, r2, 0)).ShouldBe(0);
        var v2 = await w.ViewAsync(U);
        v2.Frozen.ShouldBe(0);
        v2.Balance.ShouldBe(700);

        await using var db = await Db.CreateDbContextAsync();
        // ThenBy(Amount) 消同 Kind 平局的不稳定序（FrozenClock 下两笔 Reserve 的 CreatedAt 相同，
        // 纯 Kind 排序的平局序无契约保证，OCR R2 修复）
        var kinds = await db.Ledger.Where(e => e.UserId == U).OrderBy(e => e.Kind).ThenBy(e => e.Amount)
            .Select(e => new { e.Kind, e.Amount }).ToListAsync();
        kinds.Select(k => (k.Kind, k.Amount)).ShouldBe(new[]
        {
            (LedgerKind.Credit, 1000L), (LedgerKind.Reserve, 50L), (LedgerKind.Reserve, 300L),
            (LedgerKind.Settle, 300L), (LedgerKind.Release, 50L),
        });
    }

    [Fact]
    public async Task Chat_with_no_balance_never_reaches_upstream_and_settles_after()
    {
        var w = NewWallet();
        var billing = new ChatBilling(w, NewUsage(), Opt);
        var req = JsonNode.Parse("""{"messages":[{"role":"user","content":"hi"}]}""")!.AsObject();
        (await Should.ThrowAsync<GatewayException>(() => billing.BeginAsync(U, req, null))).Code.ShouldBe("INSUFFICIENT_BALANCE");

        await w.CreditAsync(U, 100_000, "t-" + Guid.NewGuid());
        var t = await billing.BeginAsync(U, req, "bot1");
        t.ResponseId.ShouldStartWith("hb-");
        (await billing.SettleAsync(t, 12, 8)).ShouldBe(20);
        var v = await w.ViewAsync(U);
        v.Frozen.ShouldBe(0);
        v.Balance.ShouldBe(100_000 - 20);
        var usage = await NewUsage().RecentAsync(U, 10);
        usage.Single().Bot.ShouldBe("bot1");
    }

    [Fact]
    public async Task Estimate_counts_non_message_fields_so_tools_heavy_body_reserves_more()
    {
        // NV-09 回归锁：上游按整个请求体计费（tools 等非消息字段原样转发），预估只数
        // messages[].content 会结构性低估 → 结算 clamp 到预占额 = 平台倒贴上游差额。
        // 修复后非消息字段的序列化质量计入估算（宁多冻不少冻）。
        var tiny = JsonNode.Parse("""{"messages":[{"role":"user","content":"hi"}]}""")!.AsObject();
        var toolsHeavy = JsonNode.Parse("""
            {
              "messages": [{"role":"user","content":"hi"}],
              "tools": [{"type":"function","function":{"name":"f","parameters":{"type":"object","properties":{"a":{"type":"string","description":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}}}}]
            }
            """)!.AsObject();

        var baseEst = ChatBilling.EstimatePromptTokens(tiny);
        var toolsEst = ChatBilling.EstimatePromptTokens(toolsHeavy);
        (toolsEst - baseEst).ShouldBeGreaterThan(20, "tools 字段质量必须计入估算");

        var w = NewWallet();
        await w.CreditAsync(U, 1_000_000, "t-" + Guid.NewGuid());
        var billing = new ChatBilling(w, NewUsage(), Opt);
        var ticket = await billing.BeginAsync(U, toolsHeavy, null);
        var reserved = (await w.ViewAsync(U)).Frozen;
        reserved.ShouldBeGreaterThan(
            Math.Max(1, ChatBilling.Cost(ticket.Alias, baseEst, ticket.UpstreamBody["max_tokens"]!.GetValue<long>())),
            "预占额必须高于仅按消息内容估算的口径");
        await billing.FailAsync(ticket);
    }

    [Fact]
    public async Task Stream_abort_after_delivered_frames_charges_estimate_instead_of_full_release()
    {
        // NV-10 回归锁：流式中客户端断开——已 flush 的帧真实消耗上游计费，修复前走 FailAsync
        // 全额解冻 = 已交付内容零计费。修复后 SettleAbortedAsync 按粗估收费（clamp 到预占额），
        // 用量行 success=false 区分正常完成；零交付（首帧前断开）仍走 FailAsync 全额解冻。
        var w = NewWallet();
        await w.CreditAsync(U, 100_000, "t-" + Guid.NewGuid());
        var billing = new ChatBilling(w, NewUsage(), Opt);
        var req = JsonNode.Parse("""{"messages":[{"role":"user","content":"hi"}]}""")!.AsObject();
        var ticket = await billing.BeginAsync(U, req, null);
        var reserved = (await w.ViewAsync(U)).Frozen;
        reserved.ShouldBeGreaterThan(0);

        // 有已交付内容的中断：按粗估结算而非全额解冻
        await billing.SettleAbortedAsync(ticket, promptTokens: 20, completionTokens: 40);
        var afterAbort = await w.ViewAsync(U);
        afterAbort.Frozen.ShouldBe(0, "断开后预占必须关单");
        afterAbort.Balance.ShouldBe(100_000 - 60);
        var usage = await NewUsage().RecentAsync(U, 10);
        var row = usage.Single();
        row.Success.ShouldBeFalse("中断单与正常完成单在对账口径上可区分");
        row.PromptTokens.ShouldBe(20);
        row.CompletionTokens.ShouldBe(40);

        // 对照：零交付（首帧前断开）仍全额解冻
        var t2 = await billing.BeginAsync(U, req, null);
        await billing.FailAsync(t2);
        (await w.ViewAsync(U)).Balance.ShouldBe(100_000 - 60);
    }

    [Fact]
    public async Task Failed_call_fully_unfreezes()
    {
        var w = NewWallet();
        await w.CreditAsync(U, 100_000, "t-" + Guid.NewGuid());
        var billing = new ChatBilling(w, NewUsage(), Opt);
        var t = await billing.BeginAsync(U, JsonNode.Parse("""{"messages":[]}""")!.AsObject(), null);
        (await w.ViewAsync(U)).Frozen.ShouldBeGreaterThan(0);
        await billing.FailAsync(t);
        (await w.ViewAsync(U)).Balance.ShouldBe(100_000);
        (await w.ViewAsync(U)).Frozen.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_or_missing_key_is_rejected_and_key_is_reused()
    {
        var keys = NewKeys();
        var missing = await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync(null, true));
        missing.Code.ShouldBe("INVALID_KEY");
        missing.Status.ShouldBe(401);
        (await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync("hb-nope", true))).Status.ShouldBe(401);
        var key = await keys.IssueOrReuseKeyAsync(U);
        key.ShouldStartWith("hb-");
        (await NewKeys().IssueOrReuseKeyAsync(U)).ShouldBe(key);
        (await keys.ResolveAsync(key, false)).ShouldBe(U);
        var (session, _) = await keys.IssueSessionAsync(U);
        await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync(session, allowSession: false));
        (await keys.ResolveAsync(session, true)).ShouldBe(U);
        Clock.Now = Clock.Now.AddHours(Opt.Value.SessionHours + 1);
        await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync(session, true));
        await keys.RevokeKeyAsync(U);
        await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync(key, false));
    }

    [Fact]
    public async Task Deactivated_identity_rejects_existing_key_and_session_at_use_time()
    {
        // NV-08 回归锁：凭证签发时刻查过 IsActive 不代表此刻有效——hb- 无到期、hbs- 最长一周，
        // 停用/锁定账户的存量凭证必须在使用时刻过身份状态门（与未知 Key 同错 401，不泄露状态差异）。
        var keys = NewKeys();
        var key = await keys.IssueOrReuseKeyAsync(U);
        var (session, _) = await keys.IssueSessionAsync(U);
        (await keys.ResolveAsync(key, true)).ShouldBe(U);

        UserGate.Usable = false; // 模拟停用/锁定/已删

        var hb = await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync(key, true));
        hb.Status.ShouldBe(401);
        hb.Code.ShouldBe("INVALID_KEY");
        (await Should.ThrowAsync<GatewayException>(() => keys.ResolveAsync(session, true))).Status.ShouldBe(401);
        UserGate.Calls.ShouldBeGreaterThanOrEqualTo(2, "hb- 与 hbs- 命中后都必须过身份状态门");

        UserGate.Usable = true; // 恢复（如管理员重新启用）
        (await keys.ResolveAsync(key, true)).ShouldBe(U);
    }

    [Fact]
    public async Task Order_pending_to_paid_credits_once_even_concurrently()
    {
        var (orders, w) = NewOrders();
        var o = await orders.CreateAsync(U, "p98", PayMethod.Wechat);
        o.Status.ShouldBe(OrderStatus.Pending);
        o.AmountCny.ShouldBe(98m);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => orders.MarkPaidAsync(o.OrderId)));
        (await orders.GetAsync(U, o.OrderId)).Status.ShouldBe(OrderStatus.Paid);
        (await w.ViewAsync(U)).Balance.ShouldBe(9_800_000);
        (await w.ViewAsync(U)).Percent.ShouldBe(100);
        await Should.ThrowAsync<GatewayException>(() => orders.RefreshQrAsync(U, o.OrderId));
        (await w.ReconcileAsync(U)).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Order_expires_then_refresh_returns_pending()
    {
        var (orders, _) = NewOrders();
        var o = await orders.CreateAsync(U, "p30", PayMethod.Alipay);
        Clock.Now = Clock.Now.AddSeconds(301);
        (await orders.GetAsync(U, o.OrderId)).Status.ShouldBe(OrderStatus.Expired);
        var r = await orders.RefreshQrAsync(U, o.OrderId);
        r.Status.ShouldBe(OrderStatus.Pending);
        r.QrVersion.ShouldBe(2);
        r.ExpiresAt.ShouldBeGreaterThan(Clock.Now.UtcDateTime);
    }

    [Fact]
    public async Task Order_failed_is_terminal_and_owner_scoped()
    {
        var (orders, w) = NewOrders();
        var o = await orders.CreateAsync(U, "p30", PayMethod.Wechat);
        (await Should.ThrowAsync<GatewayException>(() => orders.GetAsync(Guid.NewGuid(), o.OrderId))).Code.ShouldBe("NOT_FOUND");
        await orders.MarkFailedAsync(o.OrderId);
        await Should.ThrowAsync<GatewayException>(() => orders.MarkPaidAsync(o.OrderId));
        (await w.ViewAsync(U)).Balance.ShouldBe(0);
        (await Should.ThrowAsync<GatewayException>(() => orders.CreateAsync(U, "nope", PayMethod.Wechat))).Code.ShouldBe("BAD_REQUEST");
    }

    [Fact]
    public async Task Data_survives_new_store_instances()
    {
        await NewWallet().CreditAsync(U, 777, "t-" + Guid.NewGuid());
        var key = await NewKeys().IssueOrReuseKeyAsync(U);
        // 模拟进程重启：全新实例，只靠库
        (await NewWallet().ViewAsync(U)).Balance.ShouldBe(777);
        (await NewKeys().ResolveAsync(key, false)).ShouldBe(U);
    }

    [Theory]
    [InlineData("Development", PaymentMode.Stub, true)]
    [InlineData("Production", PaymentMode.Stub, false)]
    [InlineData("Staging", PaymentMode.Stub, false)]
    [InlineData("Development", PaymentMode.Live, false)]
    public void Simulate_pay_only_in_development(string envName, PaymentMode mode, bool allowed)
    {
        var o = new TokenGatewayOptions();
        o.Payment.Mode = mode;
        WalletController.SimulatePayAllowed(new Env(envName), o).ShouldBe(allowed);
    }

    [Fact]
    public void Scrub_uses_one_id_and_hides_upstream_traces()
    {
        var a = JsonNode.Parse("""{"id":"chatcmpl-x","model":"vendor/real","system_fingerprint":"fp","provider":"p"}""")!.AsObject();
        var b = JsonNode.Parse("""{"id":"chatcmpl-y","model":"vendor/real"}""")!.AsObject();
        ChatController.Scrub(a, "hb-chat", "hb-fixed");
        ChatController.Scrub(b, "hb-chat", "hb-fixed");
        a["id"]!.GetValue<string>().ShouldBe("hb-fixed");
        b["id"]!.GetValue<string>().ShouldBe("hb-fixed");
        a["model"]!.GetValue<string>().ShouldBe("hb-chat");
        a.ContainsKey("system_fingerprint").ShouldBeFalse();
        a.ContainsKey("provider").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Development", "UPSTREAM_UNAVAILABLE", "UPSTREAM_UNAVAILABLE")]
    [InlineData("Development", "rate_limited", "RATE_LIMITED")]
    [InlineData("Development", "INTERNAL", null)]
    [InlineData("Development", null, null)]
    [InlineData("Production", "UPSTREAM_UNAVAILABLE", null)]
    [InlineData("Production", "RATE_LIMITED", null)]
    [InlineData("Staging", "RATE_LIMITED", null)]
    public void Dev_fault_only_in_development(string envName, string? header, string? expected) =>
        ChatController.DevFault(new Env(envName), header).ShouldBe(expected);

    [Fact]
    public void Fault_error_bodies_carry_only_code_and_chinese_message()
    {
        foreach (var e in new[] { GatewayException.UpstreamUnavailable(), GatewayException.RateLimited() })
        {
            var r = (Microsoft.AspNetCore.Mvc.ObjectResult)GatewayControllerBase.Error(e);
            var json = System.Text.Json.JsonSerializer.Serialize(r.Value);
            json.ShouldNotContain("model");
            json.ShouldNotContain("provider");
            json.ShouldNotContain("channel");
            e.Message.ShouldNotContain("upstream", Case.Insensitive);
        }
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
