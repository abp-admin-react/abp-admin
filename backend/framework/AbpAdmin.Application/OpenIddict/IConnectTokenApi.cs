using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using HttpAgent;
using Volo.Abp;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 标准 /connect/token 端点声明式客户端（HttpAgent），T2.7 impersonation 与 T2.9 GenerateAccessToken 共用，
/// 替代原先两处近乎复制的手写 HttpClient form 组装 + JSON 解析。
/// 基地址由 "AuthServerTokenExchange" 命名 HttpClient 的 BaseAddress 提供
/// （AbpAdminApplicationModule 从 AuthServer:Authority 读取设置，调用方仍保留未配置守卫给出友好错误；
/// 不能用 [BaseAddress("[[AuthServer:Authority]]")]——实测 2.2.0 的特性占位符不解析配置，直接当字面量拼 URI）；
/// 该命名 HttpClient 的 30s 超时 + Development 跳过自签证书注册保持不变。
/// 返回 HttpRemoteResult：非 2xx 不抛异常，调用方读 OAuth error 字段（保持原语义）；
/// 非 JSON 错误体由 TolerantJsonContentConverter 兜底为 null Result（状态码仍在，可回落 "HTTP {status}"）。
/// </summary>
[HttpClientName(AbpAdminApplicationModule.AuthServerTokenExchangeHttpClient)]
public interface IConnectTokenApi : IHttpDeclarative
{
    // 相对地址不带前导斜杠：RFC 3986 相对引用与 BaseAddress 合并时保留基地址路径，
    // 带前导斜杠会丢弃路径（如 Authority=https://host/auth 会拼成 https://host/connect/token），
    // 不带斜杠两种 Authority 形态都与原字符串拼接 authority.TrimEnd('/') + "/connect/token" 等价
    [Post("connect/token")]
    Task<HttpRemoteResult<ConnectTokenEndpointResponse>> ExchangeAsync(
        [FormUrlEncodedBody] Dictionary<string, string> form,
        [Header(AliasAs = "Authorization")] string? authorization = null,
        // 租户上下文经请求头传递（ABP 多租户中间件按 __tenant 头解析）：
        // 匿名换票（passwordless/linked-account 中 host 侧）没有当前用户可依赖时必须显式携带，
        // null 表示 host 上下文。可选参数，既有调用点（不传 tenantId）行为不变。
        [Header(AliasAs = "__tenant")] string? tenantId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// /connect/token 成功与错误响应的最小并集（成功分支 access_token 等，错误分支 error/error_description）。
/// 字段全部可空：非 2xx 时只填错误字段，成功时只填令牌字段。
/// </summary>
public sealed class ConnectTokenEndpointResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

internal static class ConnectTokenApiExtensions
{
    /// <summary>
    /// 标准 OAuth 错误响应 → "error: description"。error 缺失时退回 description（对齐 impersonation
    /// 原逻辑 description ?? error）；两者都缺失（非 JSON 错误体，如网关错误页）返回 null，
    /// 调用方回落 HTTP 状态码——与原两处 TryReadTokenError 的行为等价并收拢为一份。
    /// 文案来自远端响应体（网关/外部 IdP 可能注入任意文本），截断防无界反射到 API 错误消息。
    /// </summary>
    public static string? ReadOAuthError(this ConnectTokenEndpointResponse? response)
    {
        if (response == null)
        {
            return null;
        }

        string? reason;
        if (response.Error.IsNullOrWhiteSpace())
        {
            reason = response.ErrorDescription.IsNullOrWhiteSpace() ? null : response.ErrorDescription;
        }
        else
        {
            reason = response.ErrorDescription.IsNullOrWhiteSpace()
                ? response.Error
                : $"{response.Error}: {response.ErrorDescription}";
        }

        return reason is { Length: > 200 } ? reason[..200] : reason;
    }
}
