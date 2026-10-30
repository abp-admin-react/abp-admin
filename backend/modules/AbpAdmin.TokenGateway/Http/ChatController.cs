using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Text.Json.Nodes;
using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Http;

/// <summary>OpenAI 兼容入口。客户端 BaseURL = {host}/v1，Bearer = hb-xxx。</summary>
[Route("v1")]
public class ChatController(KeyStore keys, ChatBilling billing, IUpstream upstream,
    IOptions<TokenGatewayOptions> options, IWebHostEnvironment env, ILogger<ChatController> log) : GatewayControllerBase
{
    /// <summary>
    /// 对话请求体上限（2MB，正常对话 KB 量级、长上下文也足够）：宿主为文件上传把 Kestrel 全局
    /// 上限放到 200MB，本端点若继承该上限，持 Key 主体可用巨体 JSON + JsonNode 全量物化
    /// 钉住数倍内存——这里显式收紧。
    /// </summary>
    public const int MaxChatBodyBytes = 2_000_000;

    [HttpGet("models")]
    public Task<IActionResult> Models() => Run(async () =>
    {
        await keys.ResolveAsync(Bearer(), allowSession: false);
        var data = options.Value.EffectiveAliases.Select(a => new { id = a.Alias, @object = "model", owned_by = "harnessbot" });
        return Ok(new { @object = "list", data });
    }, log);

    /// <summary>
    /// 对话代理。顺序：鉴权（仅 hb-，会话 hbs- 不认 → 401 INVALID_KEY）→ 预占（不足 → 402 INSUFFICIENT_BALANCE）
    /// → 转发 one-api → 结算（按 usage；流式缺 usage 时按字符粗估，最多扣到预占额）。
    /// 任一步失败且已预占：全额解冻（不是退款）。上游原文（模型/渠道/URL/错误文本）一律不出本方法。
    /// 流式已开始后出错：不能再改状态码，改为追加一帧 data:{error:{code,message}} 后结束。
    /// </summary>
    [HttpPost("chat/completions")]
    [Microsoft.AspNetCore.Mvc.RequestSizeLimit(MaxChatBodyBytes)]
    public async Task Completions()
    {
        ChatTicket? ticket = null;
        var started = false;
        // 流式进度账：提到 try 外，catch 的中断结算要读已交付字符
        long prompt = 0, completion = 0, seenChars = 0;
        var gotUsage = false;
        try
        {
            var user = await keys.ResolveAsync(Bearer(), allowSession: false);
            var request = await JsonNode.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted) as JsonObject
                          ?? throw GatewayException.BadRequest("body 必须是 JSON 对象");
            var fault = DevFault(env, Request.Headers[DevFaultHeader].FirstOrDefault());
            if (fault == GatewayErrorCodes.RateLimited) throw GatewayException.RateLimited();
            ticket = await billing.BeginAsync(user, request, Request.Headers["X-HB-Bot"].FirstOrDefault());
            if (fault == GatewayErrorCodes.UpstreamUnavailable) throw GatewayException.UpstreamUnavailable();

            using var resp = await upstream.SendAsync(ticket.UpstreamBody, HttpContext.RequestAborted);
            if (!resp.IsSuccessStatusCode)
            {
                // 内部日志只记状态码 + 对外别名 + 内部模型名（排障用），上游响应体不读、不记；
                // 对外统一改写：429 → RATE_LIMITED，其余 → UPSTREAM_UNAVAILABLE（见 UpstreamErrors）。
                log.LogWarning("gateway upstream status {Status} alias {Alias} model {Model}", (int)resp.StatusCode, ticket.Alias.Alias, ticket.Alias.Model);
                throw UpstreamErrors.FromStatus((int)resp.StatusCode);
            }

            var isStream = ticket.UpstreamBody["stream"] is System.Text.Json.Nodes.JsonValue streamValue
                           && streamValue.TryGetValue<bool>(out var streamFlag) && streamFlag;
            if (!isStream)
            {
                var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(HttpContext.RequestAborted)) as JsonObject
                           ?? throw GatewayException.UpstreamUnavailable();
                if (UpstreamErrors.FromBody(json) is { } bodyError) throw bodyError;
                var (p, c) = ReadUsage(json);
                Scrub(json, ticket.Alias.Alias, ticket.ResponseId);
                await billing.SettleAsync(ticket, p, c);
                ticket = null;
                Response.ContentType = "application/json";
                await Response.WriteAsync(json.ToJsonString(), HttpContext.RequestAborted);
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            started = true;
            // 流空闲超时：client.Timeout 只覆盖到响应头，上游 mid-stream 停发时逐行读会无限挂起
            //（唯一解挂是客户端断开，期间预占一直冻结）。每帧读之前重置空闲计时，帧处理/写出不受影响；
            // 空闲阈值与上游超时同一口径（最小 30s）。
            using var idleCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            var idleTimeout = TimeSpan.FromSeconds(Math.Max(30, options.Value.Upstream.TimeoutSeconds));
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(HttpContext.RequestAborted), Encoding.UTF8);
            while (true)
            {
                idleCts.CancelAfter(idleTimeout);
                var line = await reader.ReadLineAsync(idleCts.Token);
                if (line is null) break;
                idleCts.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]")
                {
                    await Response.WriteAsync("data: [DONE]\n\n", HttpContext.RequestAborted);
                    break;
                }
                if (JsonNode.Parse(payload) is not JsonObject chunk) continue;
                if (UpstreamErrors.FromBody(chunk) is { } chunkError) throw chunkError;
                if (chunk["usage"] is JsonObject)
                {
                    (prompt, completion) = ReadUsage(chunk);
                    gotUsage = true;
                }
                if (chunk["choices"] is JsonArray choices)
                    foreach (var ch in choices)
                        seenChars += ch?["delta"]?["content"]?.ToString().Length ?? 0;
                Scrub(chunk, ticket.Alias.Alias, ticket.ResponseId);
                await Response.WriteAsync($"data: {chunk.ToJsonString()}\n\n", HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
            if (!gotUsage)
            {
                prompt = ChatBilling.EstimatePromptTokens(ticket.UpstreamBody);
                completion = (seenChars + 1) / 2;
            }
            await billing.SettleAsync(ticket, prompt, completion);
            ticket = null;
        }
        catch (Exception e)
        {
            // ticket 非空 = 已预占未结算：先全额解冻，保证每单只关一次（Wallet 内条件 UPDATE 兜底）。
            // 解冻自身失败不顶掉对客错误输出——预占单留 Open 由 ReservationJanitor 兜底解冻
            if (ticket is not null)
            {
                try
                {
                    // 流式中客户端断开——已 flush 到线上的帧真实消耗了上游计费，无条件
                    // 无条件全额解冻 = 已交付内容零计费。有交付时按「输入粗估 + 已见输出字符
                    // 折算」结算（clamp 到预占额），用量行记 success=false 区分正常完成；
                    // 未开始流式或零交付（首帧前断开）仍走全额解冻。
                    if (started && seenChars > 0)
                    {
                        var promptEst = gotUsage ? prompt : ChatBilling.EstimatePromptTokens(ticket.UpstreamBody);
                        var completionEst = gotUsage ? completion : (seenChars + 1) / 2;
                        await billing.SettleAbortedAsync(ticket, promptEst, completionEst);
                    }
                    else
                    {
                        await billing.FailAsync(ticket);
                    }
                }
                catch (Exception failEx)
                {
                    log.LogError(failEx, "gateway chat fail-unfreeze failed");
                }
            }
            // 客户端断开：已解冻，无人接收响应，静默结束。
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            var ge = UpstreamErrors.FromException(e);
            // GatewayException 是预期业务错误（余额不足/限流/上游映射），不记 Error；
            // 其他异常记 Error 带堆栈，仅进内部日志，对外只给映射后的错误码与文案。
            if (e is not GatewayException) log.LogError(e, "gateway chat failed");
            if (started)
            {
                // 用 System.Text.Json 序列化而不是手拼：ge.Message 一旦未来动态化（如带上游摘要）
                // 手拼既会破坏 SSE 分帧也会注入非法 JSON——这里从源头堵死
                var frame = new System.Text.Json.Nodes.JsonObject
                {
                    ["error"] = new System.Text.Json.Nodes.JsonObject { ["code"] = ge.Code, ["message"] = ge.Message },
                };
                await Response.WriteAsync("data: " + frame.ToJsonString() + "\n\n", HttpContext.RequestAborted);
                return;
            }
            await Error(ge).ExecuteResultAsync(ControllerContext);
        }
    }

    public const string DevFaultHeader = "X-HB-Dev-Fault";

    /// <summary>
    /// 联调用故障注入：仅 Development 生效，其他环境恒为 null（头被忽略）。
    /// RATE_LIMITED 在预占前抛（同真实限流）；UPSTREAM_UNAVAILABLE 在预占后抛，走全额解冻（同真实上游失败）。
    /// </summary>
    internal static string? DevFault(IHostEnvironment env, string? header)
    {
        if (!env.IsDevelopment() || string.IsNullOrWhiteSpace(header)) return null;
        return header.Trim().ToUpperInvariant() switch
        {
            GatewayErrorCodes.UpstreamUnavailable => GatewayErrorCodes.UpstreamUnavailable,
            GatewayErrorCodes.RateLimited => GatewayErrorCodes.RateLimited,
            _ => null,
        };
    }

    private static (long Prompt, long Completion) ReadUsage(JsonObject o) =>
        (o["usage"]?["prompt_tokens"]?.GetValue<long>() ?? 0, o["usage"]?["completion_tokens"]?.GetValue<long>() ?? 0);

    /// <summary>
    /// 剥离上游痕迹：白名单放行 OpenAI 兼容字段——上游/渠道（one-api 及其后端）新增的任何
    /// 字段（路由、渠道名、自定义 meta 等）一律出不去，模块不变量「上游原文不出本方法」
    /// 不依赖追补黑名单。取舍：上游若新增合法业务字段（如 message.annotations），客户端
    /// 也拿不到——有意为之，放行需显式加进 AllowedResponseFields 并过一遍安全评审。
    /// model 回别名，id 换成本次请求固定的 hb- id（流式各帧一致）。
    /// </summary>
    internal static void Scrub(JsonObject o, string alias, string responseId)
    {
        o["model"] = alias;
        foreach (var k in o.ToArray())
        {
            if (!AllowedResponseFields.Contains(k.Key)) o.Remove(k.Key);
        }
        if (o["id"] is not null) o["id"] = responseId;
    }

    private static readonly HashSet<string> AllowedResponseFields = new(StringComparer.Ordinal)
    {
        "id", "object", "created", "model", "choices", "usage",
    };
}
