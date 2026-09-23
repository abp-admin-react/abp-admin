using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HttpAgent;

namespace AbpAdmin.Captcha;

/// <summary>"Turnstile" 命名 HttpClient 的名字锚点（注册/绑定/测试三处共用，防字符串漂移——
/// 未注册名 CreateClient 会静默回落无定制默认客户端）。注册点：HttpRemoteConfigurator。</summary>
public static class TurnstileHttpClients
{
    public const string SiteVerify = "Turnstile";
}

/// <summary>
/// Cloudflare Turnstile siteverify 声明式客户端（HttpAgent）。
/// 绑定 "Turnstile" 命名 HttpClient（注册点：HttpRemoteConfigurator）。
/// 返回 HttpRemoteResult：非 2xx 不抛异常，由调用方按 IsSuccessStatusCode / Success 判定，
/// 与原手写 HttpClient 版"读响应体判定、不 EnsureSuccess"的语义一致。
/// </summary>
[HttpClientName(TurnstileHttpClients.SiteVerify)]
public interface ITurnstileSiteVerifyApi : IHttpDeclarative
{
    [Post("https://challenges.cloudflare.com/turnstile/v0/siteverify")]
    Task<HttpRemoteResult<TurnstileVerifyResponse>> VerifyAsync(
        [FormUrlEncodedBody] Dictionary<string, string> form);
}

/// <summary>siteverify 响应（只取校验关心的字段）。</summary>
public sealed class TurnstileVerifyResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; set; }
}
