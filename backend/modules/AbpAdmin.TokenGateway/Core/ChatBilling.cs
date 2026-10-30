using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public sealed record ChatTicket(Guid User, Guid Reservation, AliasOption Alias, string? Bot, JsonObject UpstreamBody, string ResponseId);

/// <summary>对话计费：选型（别名→内部模型）、reserve、settle、用量记录。</summary>
public class ChatBilling(Wallet wallet, UsageLog usage, IOptions<TokenGatewayOptions> options)
{
    /// <summary>单用户同时在途对话上限（进程内计数，单实例口径，扩多实例前需换 Redis 计数——KeyPrefix 已隔离）；超出 → 429 RATE_LIMITED，且不预占。</summary>
    public const int MaxInflightPerUser = 4;
    private readonly ConcurrentDictionary<Guid, int> _inflight = new();

    /// <summary>
    /// 选别名（未知/缺省 → DefaultAlias）→ 限并发 → 按「输入粗估 + max_tokens」预占（至少 1）→ 生成上游请求体：
    /// model 换成内部模型、去掉 user、流式强制 include_usage。返回的 ResponseId 是本次固定的 hb- id。
    /// 上游体构造与入参解析全部放在预占之前（不会留下「已预占未释放」的孤儿单）。
    /// 入参类型从严：max_tokens/stream 键存在但类型非法时直接 400（TryGetValue
    /// 失败静默回落默认值/按非流式处理，客户端误用无感且注释与行为不符）。
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
            long maxTokens;
            if (request["max_tokens"] is null)
            {
                maxTokens = alias.DefaultMaxTokens;
            }
            else if (request["max_tokens"] is System.Text.Json.Nodes.JsonValue mv && mv.TryGetValue<int>(out var mt) && mt >= 1)
            {
                maxTokens = mt;
            }
            else
            {
                throw GatewayException.BadRequest("max_tokens 必须是正整数");
            }
            var body = (JsonObject)request.DeepClone();
            body["model"] = alias.Model;
            body["max_tokens"] = maxTokens;
            body.Remove("user");
            bool? stream = body["stream"] is { } streamNode
                ? (streamNode is System.Text.Json.Nodes.JsonValue sv && sv.TryGetValue<bool>(out var streamFlag)
                    ? streamFlag
                    : throw GatewayException.BadRequest("stream 必须是布尔"))
                : null;
            if (stream == true)
                body["stream_options"] = new JsonObject { ["include_usage"] = true };

            var estimate = Cost(alias, EstimatePromptTokens(request), maxTokens);
            var reservation = await wallet.ReserveAsync(user, Math.Max(1, estimate), alias.Alias);
            try
            {
                return new ChatTicket(user, reservation, alias, bot, body, "hb-" + Guid.NewGuid().ToString("N")[..24]);
            }
            catch
            {
                // ChatTicket 是纯 record 构造不会抛；防御性兜底：已预占必须解冻，绝不留孤儿单
                await wallet.ReleaseAsync(user, reservation);
                throw;
            }
        }
        catch
        {
            Done(user);
            throw;
        }
    }

    /// <summary>按实际用量结算（Wallet 内最多扣到预占额，差额解冻）并记一条成功用量；返回实扣代币。
    /// 并发额度在成功路径末尾才释放：中途抛异常时由调用方 FailAsync 统一 Done+Release，
    /// 不会像「先 Done 后结算」那样在 Settle 抛出时被 catch 再 Done 一次（双扣稀释限流）。</summary>
    public async Task<long> SettleAsync(ChatTicket t, long promptTokens, long completionTokens)
    {
        var charged = await wallet.SettleAsync(t.User, t.Reservation, Cost(t.Alias, promptTokens, completionTokens));
        await usage.AddAsync(t.User, t.Alias.Alias, t.Bot, promptTokens, completionTokens, charged, true);
        Done(t.User);
        return charged;
    }

    /// <summary>调用失败/上游未计费：全额解冻（不是退款）。</summary>
    public async Task FailAsync(ChatTicket t)
    {
        Done(t.User);
        await wallet.ReleaseAsync(t.User, t.Reservation);
        await usage.AddAsync(t.User, t.Alias.Alias, t.Bot, 0, 0, 0, false);
    }

    /// <summary>
    /// 流式中断结算：客户端中途断开时，已 flush 到线上的帧真实消耗了上游计费，
    /// 全额解冻 = 白嫖已交付内容。按「输入粗估 + 已见输出字符折算」结算（clamp 到预占额），
    /// 用量行仍记 success=false（流未正常完成，对账口径区分于成功单）。并发额度照常释放。
    /// </summary>
    public async Task<long> SettleAbortedAsync(ChatTicket t, long promptTokens, long completionTokens)
    {
        var charged = await wallet.SettleAsync(t.User, t.Reservation, Cost(t.Alias, promptTokens, completionTokens));
        await usage.AddAsync(t.User, t.Alias.Alias, t.Bot, promptTokens, completionTokens, charged, false);
        Done(t.User);
        return charged;
    }

    private void Done(Guid user) => _inflight.AddOrUpdate(user, 0, (_, n) => Math.Max(0, n - 1));

    internal static long Cost(AliasOption a, long prompt, long completion) =>
        (long)Math.Ceiling(prompt * a.PromptRate + completion * a.CompletionRate);

    /// <summary>粗估输入 token：字符数/2 向上取整（中文偏保守），宁多冻不少冻。
    /// 除 messages[].content 外，其余顶层字段（tools/tool_choice/response_format 等）的序列化
    /// 质量同样计入：上游按整个请求体计费（BeginAsync 原样转发），只数消息内容会结构性低估，
    /// 结算被 clamp 到预占额 ⇒ 差额平台倒贴。</summary>
    internal static long EstimatePromptTokens(JsonObject request)
    {
        long chars = 0;
        if (request["messages"] is JsonArray msgs)
            foreach (var m in msgs)
                chars += m?["content"]?.ToJsonString().Length ?? 0;
        long otherChars = 0;
        foreach (var kv in request)
        {
            if (kv.Key == "messages") continue;
            otherChars += kv.Value?.ToJsonString().Length ?? 0;
        }
        return (chars + otherChars + 1) / 2 + 16;
    }
}
