using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public sealed record ChatTicket(Guid User, Guid Reservation, AliasOption Alias, string? Bot, JsonObject UpstreamBody, string ResponseId);

/// <summary>对话计费：选型（别名→内部模型）、reserve、settle、用量记录。</summary>
public class ChatBilling(Wallet wallet, UsageLog usage, IOptions<TokenGatewayOptions> options)
{
    /// <summary>单用户同时在途对话上限（进程内计数，单实例口径）；超出 → 429 RATE_LIMITED，且不预占。</summary>
    public const int MaxInflightPerUser = 4;
    private readonly ConcurrentDictionary<Guid, int> _inflight = new();

    /// <summary>
    /// 选别名（未知/缺省 → DefaultAlias）→ 限并发 → 按「输入粗估 + max_tokens」预占（至少 1）→ 生成上游请求体：
    /// model 换成内部模型、去掉 user、流式强制 include_usage。返回的 ResponseId 是本次固定的 hb- id。
    /// </summary>
    public async Task<ChatTicket> BeginAsync(Guid user, JsonObject request, string? bot)
    {
        var o = options.Value;
        var aliasName = request["model"]?.GetValue<string>();
        var alias = o.EffectiveAliases.FirstOrDefault(a => a.Alias == aliasName)
                    ?? o.EffectiveAliases.First(a => a.Alias == o.DefaultAlias);

        if (_inflight.AddOrUpdate(user, 1, (_, n) => n + 1) > MaxInflightPerUser)
        {
            Done(user);
            throw GatewayException.RateLimited();
        }

        try
        {
            var maxTokens = request["max_tokens"]?.GetValue<int>() ?? alias.DefaultMaxTokens;
            var estimate = Cost(alias, EstimatePromptTokens(request), maxTokens);
            var reservation = await wallet.ReserveAsync(user, Math.Max(1, estimate), alias.Alias);

            var body = (JsonObject)request.DeepClone();
            body["model"] = alias.Model;
            body["max_tokens"] = maxTokens;
            body.Remove("user");
            if (body["stream"]?.GetValue<bool>() == true)
                body["stream_options"] = new JsonObject { ["include_usage"] = true };
            return new ChatTicket(user, reservation, alias, bot, body, "hb-" + Guid.NewGuid().ToString("N")[..24]);
        }
        catch
        {
            Done(user);
            throw;
        }
    }

    /// <summary>按实际用量结算（Wallet 内最多扣到预占额，差额解冻）并记一条成功用量；返回实扣代币。</summary>
    public async Task<long> SettleAsync(ChatTicket t, long promptTokens, long completionTokens)
    {
        Done(t.User);
        var charged = await wallet.SettleAsync(t.User, t.Reservation, Cost(t.Alias, promptTokens, completionTokens));
        await usage.AddAsync(t.User, t.Alias.Alias, t.Bot, promptTokens, completionTokens, charged, true);
        return charged;
    }

    /// <summary>调用失败/上游未计费：全额解冻（不是退款）。</summary>
    public async Task FailAsync(ChatTicket t)
    {
        Done(t.User);
        await wallet.ReleaseAsync(t.User, t.Reservation);
        await usage.AddAsync(t.User, t.Alias.Alias, t.Bot, 0, 0, 0, false);
    }

    private void Done(Guid user) => _inflight.AddOrUpdate(user, 0, (_, n) => Math.Max(0, n - 1));

    internal static long Cost(AliasOption a, long prompt, long completion) =>
        (long)Math.Ceiling(prompt * a.PromptRate + completion * a.CompletionRate);

    /// <summary>粗估输入 token：字符数/2 向上取整（中文偏保守），宁多冻不少冻。</summary>
    internal static long EstimatePromptTokens(JsonObject request)
    {
        long chars = 0;
        if (request["messages"] is JsonArray msgs)
            foreach (var m in msgs)
                chars += m?["content"]?.ToJsonString().Length ?? 0;
        return (chars + 1) / 2 + 16;
    }
}
