using System.Net;
using System.Text.Json.Nodes;
using AbpAdmin.TokenGateway.Core;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.TokenGateway;

public class UpstreamErrorsTests
{
    private const string Secret = "deepseek-ai/DeepSeek-V3 channel#7 https://api.vendor.example/v1 sk-internal";

    [Theory]
    [InlineData(429, "RATE_LIMITED", 429)]
    [InlineData(400, "UPSTREAM_UNAVAILABLE", 503)]
    [InlineData(401, "UPSTREAM_UNAVAILABLE", 503)]
    [InlineData(403, "UPSTREAM_UNAVAILABLE", 503)]
    [InlineData(404, "UPSTREAM_UNAVAILABLE", 503)]
    [InlineData(500, "UPSTREAM_UNAVAILABLE", 503)]
    [InlineData(502, "UPSTREAM_UNAVAILABLE", 503)]
    [InlineData(503, "UPSTREAM_UNAVAILABLE", 503)]
    public void Status_maps_to_our_code(int status, string code, int ours)
    {
        var e = UpstreamErrors.FromStatus(status);
        e.Code.ShouldBe(code);
        e.Status.ShouldBe(ours);
    }

    [Theory]
    [InlineData("""{"error":{"message":"X","type":"one_api_error","code":"rate_limit_exceeded"}}""", "RATE_LIMITED")]
    [InlineData("""{"error":{"message":"X","type":"rate_limit_error"}}""", "RATE_LIMITED")]
    [InlineData("""{"error":{"message":"X","type":"one_api_error","code":429}}""", "RATE_LIMITED")]
    [InlineData("""{"error":{"message":"X","type":"one_api_error","code":"insufficient_user_quota"}}""", "UPSTREAM_UNAVAILABLE")]
    [InlineData("""{"error":{"message":"X","type":"one_api_error","code":"pre_consume_token_quota_failed"}}""", "UPSTREAM_UNAVAILABLE")]
    [InlineData("""{"error":{"message":"X","type":"new_api_error","code":"model_not_found"}}""", "UPSTREAM_UNAVAILABLE")]
    [InlineData("""{"error":{"message":"X","type":"invalid_request_error","code":"invalid_api_key"}}""", "UPSTREAM_UNAVAILABLE")]
    [InlineData("""{"error":"X"}""", "UPSTREAM_UNAVAILABLE")]
    public void Error_body_maps_to_our_code_and_drops_upstream_text(string json, string code)
    {
        var body = JsonNode.Parse(json.Replace("X", Secret))!.AsObject();
        var e = UpstreamErrors.FromBody(body).ShouldNotBeNull();
        e.Code.ShouldBe(code);
        e.Code.ShouldNotBe(GatewayErrorCodes.InsufficientBalance);
        AssertClean(e);
    }

    [Fact]
    public void Normal_body_is_not_an_error() =>
        UpstreamErrors.FromBody(JsonNode.Parse("""{"id":"x","choices":[]}""")!.AsObject()).ShouldBeNull();

    [Fact]
    public void Exceptions_map_without_leaking_text()
    {
        UpstreamErrors.FromException(new HttpRequestException(Secret)).Code.ShouldBe("UPSTREAM_UNAVAILABLE");
        UpstreamErrors.FromException(new IOException(Secret)).Code.ShouldBe("UPSTREAM_UNAVAILABLE");
        UpstreamErrors.FromException(new System.Text.Json.JsonException(Secret)).Code.ShouldBe("UPSTREAM_UNAVAILABLE");
        UpstreamErrors.FromException(new InvalidOperationException(Secret)).Code.ShouldBe("INTERNAL");
        UpstreamErrors.FromException(GatewayException.RateLimited()).Code.ShouldBe("RATE_LIMITED");
        foreach (var e in new Exception[] { new HttpRequestException(Secret), new InvalidOperationException(Secret) })
            AssertClean(UpstreamErrors.FromException(e));
    }

    [Theory]
    [InlineData("", "tok")]
    [InlineData("http://127.0.0.1:3000", "CHANGE_ME")]
    [InlineData("http://127.0.0.1:3000", "")]
    public async Task Unconfigured_one_api_is_upstream_unavailable(string baseUrl, string token)
    {
        var up = NewUpstream(baseUrl, token, new Throwing());
        (await Should.ThrowAsync<GatewayException>(() => up.SendAsync(new JsonObject(), default))).Code.ShouldBe("UPSTREAM_UNAVAILABLE");
    }

    [Fact]
    public async Task One_api_unreachable_is_upstream_unavailable()
    {
        var up = NewUpstream("http://127.0.0.1:3000", "tok", new Throwing());
        var e = await Should.ThrowAsync<GatewayException>(() => up.SendAsync(new JsonObject(), default));
        e.Code.ShouldBe("UPSTREAM_UNAVAILABLE");
        AssertClean(e);
    }

    [Fact]
    public async Task Forwards_to_configured_base_url_with_internal_token()
    {
        var h = new Recording();
        var up = NewUpstream("http://127.0.0.1:3000/", "tok-1", h);
        using var resp = await up.SendAsync(new JsonObject { ["model"] = "m" }, default);
        h.Last!.RequestUri!.ToString().ShouldBe("http://127.0.0.1:3000/v1/chat/completions");
        h.Last.Headers.Authorization!.Parameter.ShouldBe("tok-1");
    }

    private static void AssertClean(GatewayException e)
    {
        foreach (var leak in new[] { "deepseek", "channel", "http", "sk-", "vendor", "one_api" })
            e.Message.ShouldNotContain(leak, Case.Insensitive);
    }

    private static OneApiUpstream NewUpstream(string baseUrl, string token, HttpMessageHandler handler)
    {
        var o = new TokenGatewayOptions();
        o.Upstream.BaseUrl = baseUrl;
        o.Upstream.InternalToken = token;
        return new OneApiUpstream(new Factory(handler), Options.Create(o));
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            throw new HttpRequestException(Secret);
    }

    private sealed class Recording : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Last = r;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
