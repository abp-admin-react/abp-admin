using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public sealed record ChatTicket(Guid User, Guid Reservation, AliasOption Alias, string? Bot, JsonObject UpstreamBody);

/// <summary>对话计费：选型（别名→内部模型）、reserve、settle、用量记录。</summary>
public class ChatBilling(Wallet wallet, UsageLog usage, IOptions<TokenGatewayOptions> options)
{
    public const int MaxInflightPerUser = 4;
    private readonly ConcurrentDictionary<Guid, int> _inflight = new();

    public ChatTicket Begin(Guid user, JsonObject request, string? bot)
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
            var reservation = wallet.Reserve(user, Math.Max(1, estimate));

            var body = (JsonObject)request.DeepClone();
            body["model"] = alias.Model;
            body["max_tokens"] = maxTokens;
            body.Remove("user");
            if (body["stream"]?.GetValue<bool>() == true)
                body["stream_options"] = new JsonObject { ["include_usage"] = true };
            return new ChatTicket(user, reservation, alias, bot, body);
        }
        catch
        {
            Done(user);
            throw;
        }
    }

    public long Settle(ChatTicket t, long promptTokens, long completionTokens)
    {
        Done(t.User);
        var charged = wallet.Settle(t.User, t.Reservation, Cost(t.Alias, promptTokens, completionTokens));
        usage.Add(t.User, new UsageEntry(DateTimeOffset.UtcNow, t.Alias.Alias, t.Bot, promptTokens, completionTokens, charged, true));
        return charged;
    }

    /// <summary>调用失败/上游未计费：全额解冻（不是退款）。</summary>
    public void Fail(ChatTicket t)
    {
        Done(t.User);
        wallet.Release(t.User, t.Reservation);
        usage.Add(t.User, new UsageEntry(DateTimeOffset.UtcNow, t.Alias.Alias, t.Bot, 0, 0, 0, false));
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
