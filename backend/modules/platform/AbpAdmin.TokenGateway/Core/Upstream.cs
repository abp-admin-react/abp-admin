using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AbpAdmin.TokenGateway.Core;

public interface IUpstream
{
    /// <summary>发往内网网关；body.model 已是内部模型名。返回原始响应，由出口剥离痕迹。</summary>
    Task<HttpResponseMessage> SendAsync(JsonObject body, CancellationToken ct);
}

public class OneApiUpstream(IHttpClientFactory http, IOptions<TokenGatewayOptions> options) : IUpstream
{
    public const string ClientName = "tg-upstream";

    public async Task<HttpResponseMessage> SendAsync(JsonObject body, CancellationToken ct)
    {
        var o = options.Value.Upstream;
        if (string.IsNullOrWhiteSpace(o.BaseUrl) || o.InternalToken is "" or "CHANGE_ME")
            throw GatewayException.UpstreamUnavailable();
        var client = http.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        var req = new HttpRequestMessage(HttpMethod.Post, o.BaseUrl.TrimEnd('/') + "/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.InternalToken);
        try
        {
            return await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw GatewayException.UpstreamUnavailable();
        }
    }
}

/// <summary>联调桩：不出网，回一段固定文本和 usage，用于走通 reserve→settle。</summary>
public class MockUpstream : IUpstream
{
    public const string Reply = "（联调桩回复）";

    public Task<HttpResponseMessage> SendAsync(JsonObject body, CancellationToken ct)
    {
        var stream = body["stream"]?.GetValue<bool>() == true;
        var model = body["model"]?.GetValue<string>() ?? "";
        var usage = new JsonObject { ["prompt_tokens"] = 12, ["completion_tokens"] = 8, ["total_tokens"] = 20 };
        string content;
        string type;
        if (stream)
        {
            var c1 = new JsonObject { ["id"] = "mock", ["object"] = "chat.completion.chunk", ["model"] = model,
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = Reply } }) };
            var c2 = new JsonObject { ["id"] = "mock", ["object"] = "chat.completion.chunk", ["model"] = model,
                ["choices"] = new JsonArray(), ["usage"] = usage };
            content = $"data: {c1.ToJsonString()}\n\ndata: {c2.ToJsonString()}\n\ndata: [DONE]\n\n";
            type = "text/event-stream";
        }
        else
        {
            content = new JsonObject
            {
                ["id"] = "mock", ["object"] = "chat.completion", ["model"] = model,
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0, ["finish_reason"] = "stop",
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = Reply },
                }),
                ["usage"] = usage,
            }.ToJsonString();
            type = "application/json";
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, type) });
    }
}
