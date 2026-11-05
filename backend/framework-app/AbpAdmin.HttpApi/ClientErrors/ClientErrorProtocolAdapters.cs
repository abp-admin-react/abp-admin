using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// OAuth 错误协议适配器。
/// 实现 IClientErrorProtocolAdapter 契约，处理 OAuth 端点的错误响应。
/// </summary>
public class OAuthErrorProtocolAdapter : IClientErrorProtocolAdapter
{
    public ErrorProtocol Protocol => ErrorProtocol.OAuth;

    public async Task<bool> TryWriteProtocolErrorAsync(
        HttpContext httpContext,
        ClientErrorHttpProjection projection,
        CancellationToken cancellationToken = default)
    {
        if (httpContext == null)
        {
            throw new ArgumentNullException(nameof(httpContext));
        }

        if (projection == null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        if (httpContext.Request.Path.StartsWithSegments("/connect"))
        {
            if (httpContext.Response.HasStarted)
            {
                return false;
            }

            httpContext.Response.Clear();
            httpContext.Response.StatusCode = projection.StatusCode;
            httpContext.Response.ContentType = "application/json; charset=utf-8";

            if (projection.StatusCode == 401)
            {
                httpContext.Response.Headers["WWW-Authenticate"] = "Bearer";
            }

            var oauthError = new
            {
                error = MapToOAuthError(projection.Body.Kind),
                error_description = projection.Body.Message,
            };

            await httpContext.Response.WriteAsJsonAsync(oauthError, cancellationToken);
            return true;
        }

        return false;
    }

    private static string MapToOAuthError(PublicErrorKind kind)
    {
        return kind switch
        {
            PublicErrorKind.Authentication => "access_denied",
            PublicErrorKind.Forbidden => "access_denied",
            PublicErrorKind.RateLimited => "slow_down",
            _ => "server_error",
        };
    }
}

/// <summary>
/// ABP 错误信封协议适配器（默认）。
/// </summary>
public class AbpEnvelopeProtocolAdapter : IClientErrorProtocolAdapter
{
    public ErrorProtocol Protocol => ErrorProtocol.AbpEnvelope;

    public Task<bool> TryWriteProtocolErrorAsync(
        HttpContext httpContext,
        ClientErrorHttpProjection projection,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}

/// <summary>
/// SignalR Hub 握手协议适配器。
/// </summary>
public class SignalRProtocolAdapter : IClientErrorProtocolAdapter
{
    public ErrorProtocol Protocol => ErrorProtocol.SignalR;

    public Task<bool> TryWriteProtocolErrorAsync(
        HttpContext httpContext,
        ClientErrorHttpProjection projection,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}