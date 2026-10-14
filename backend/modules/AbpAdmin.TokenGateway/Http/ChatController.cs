using Microsoft.AspNetCore.Http;
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
    IOptions<TokenGatewayOptions> options, ILogger<ChatController> log) : GatewayControllerBase
{
    [HttpGet("models")]
    public Task<IActionResult> Models() => Run(() =>
    {
        keys.Resolve(Bearer(), allowSession: false);
        var data = options.Value.EffectiveAliases.Select(a => new { id = a.Alias, @object = "model", owned_by = "harnessbot" });
        return Task.FromResult<IActionResult>(Ok(new { @object = "list", data }));
    }, log);

    [HttpPost("chat/completions")]
    public async Task Completions()
    {
        ChatTicket? ticket = null;
        var started = false;
        try
        {
            var user = keys.Resolve(Bearer(), allowSession: false);
            var request = await JsonNode.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted) as JsonObject
                          ?? throw GatewayException.BadRequest("body 必须是 JSON 对象");
            ticket = billing.Begin(user, request, Request.Headers["X-HB-Bot"].FirstOrDefault());

            using var resp = await upstream.SendAsync(ticket.UpstreamBody, HttpContext.RequestAborted);
            if (!resp.IsSuccessStatusCode)
            {
                // 原文只进内部日志，对外统一改写。
                log.LogWarning("gateway upstream status {Status} alias {Alias} model {Model}", (int)resp.StatusCode, ticket.Alias.Alias, ticket.Alias.Model);
                throw GatewayException.UpstreamUnavailable();
            }

            var isStream = ticket.UpstreamBody["stream"]?.GetValue<bool>() == true;
            if (!isStream)
            {
                var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(HttpContext.RequestAborted)) as JsonObject
                           ?? throw GatewayException.UpstreamUnavailable();
                var (p, c) = ReadUsage(json);
                Scrub(json, ticket.Alias.Alias);
                billing.Settle(ticket, p, c);
                ticket = null;
                Response.ContentType = "application/json";
                await Response.WriteAsync(json.ToJsonString(), HttpContext.RequestAborted);
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            started = true;
            long prompt = 0, completion = 0, seenChars = 0;
            var gotUsage = false;
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(HttpContext.RequestAborted), Encoding.UTF8);
            while (await reader.ReadLineAsync(HttpContext.RequestAborted) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]")
                {
                    await Response.WriteAsync("data: [DONE]\n\n", HttpContext.RequestAborted);
                    break;
                }
                if (JsonNode.Parse(payload) is not JsonObject chunk) continue;
                if (chunk["usage"] is JsonObject)
                {
                    (prompt, completion) = ReadUsage(chunk);
                    gotUsage = true;
                }
                if (chunk["choices"] is JsonArray choices)
                    foreach (var ch in choices)
                        seenChars += ch?["delta"]?["content"]?.ToString().Length ?? 0;
                Scrub(chunk, ticket.Alias.Alias);
                await Response.WriteAsync($"data: {chunk.ToJsonString()}\n\n", HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
            if (!gotUsage)
            {
                prompt = ChatBilling.EstimatePromptTokens(ticket.UpstreamBody);
                completion = (seenChars + 1) / 2;
            }
            billing.Settle(ticket, prompt, completion);
            ticket = null;
        }
        catch (Exception e)
        {
            if (ticket is not null) billing.Fail(ticket);
            if (HttpContext.RequestAborted.IsCancellationRequested) return;
            var ge = e as GatewayException ?? GatewayException.Internal();
            if (e is not GatewayException) log.LogError(e, "gateway chat failed");
            if (started)
            {
                await Response.WriteAsync($"data: {{\"error\":{{\"code\":\"{ge.Code}\",\"message\":\"{ge.Message}\"}}}}\n\n");
                return;
            }
            await Error(ge).ExecuteResultAsync(ControllerContext);
        }
    }

    private static (long Prompt, long Completion) ReadUsage(JsonObject o) =>
        (o["usage"]?["prompt_tokens"]?.GetValue<long>() ?? 0, o["usage"]?["completion_tokens"]?.GetValue<long>() ?? 0);

    /// <summary>剥离上游痕迹：model 回别名，去掉指纹/渠道类字段。</summary>
    internal static void Scrub(JsonObject o, string alias)
    {
        o["model"] = alias;
        foreach (var k in new[] { "system_fingerprint", "provider", "channel", "x_groq", "service_tier" }) o.Remove(k);
        if (o["id"] is not null) o["id"] = "hb-" + Guid.NewGuid().ToString("N")[..24];
    }
}
