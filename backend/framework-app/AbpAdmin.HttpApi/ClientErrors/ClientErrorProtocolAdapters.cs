using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// OAuth 错误协议适配器。
/// 实现 IClientErrorProtocolAdapter 契约，处理 OAuth 端点（/connect/*）的错误响应。
/// 生产路径由登记表选择本适配器；不得用 protocolOverride 把 /api 压成 OAuth。
/// 经 ABP 约定式注册为单例（无状态），显式暴露为 IClientErrorProtocolAdapter
/// （类名不符合 I{ClassName} 默认暴露约定，供 IEnumerable 聚合解析）。
/// </summary>
[ExposeServices(typeof(IClientErrorProtocolAdapter))]
public class OAuthErrorProtocolAdapter : IClientErrorProtocolAdapter, ISingletonDependency
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

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = projection.StatusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        ClientErrorHttpHeaders.ApplyRetryAfter(httpContext.Response, projection.RetryAfterSeconds);

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
/// ABP 错误信封协议适配器（默认回退，不接管任何请求——由 ResponseWriter 统一出口）。
/// 显式暴露为 IClientErrorProtocolAdapter 供聚合解析。
/// </summary>
[ExposeServices(typeof(IClientErrorProtocolAdapter))]
public class AbpEnvelopeProtocolAdapter : IClientErrorProtocolAdapter, ISingletonDependency
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
/// SignalR Hub 握手协议适配器：不套 ABP 信封，已开始则不写。
/// </summary>
[ExposeServices(typeof(IClientErrorProtocolAdapter))]
public class SignalRProtocolAdapter : IClientErrorProtocolAdapter, ISingletonDependency
{
    public ErrorProtocol Protocol => ErrorProtocol.SignalR;

    public Task<bool> TryWriteProtocolErrorAsync(
        HttpContext httpContext,
        ClientErrorHttpProjection projection,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Response.HasStarted)
        {
            return Task.FromResult(true);
        }

        httpContext.Response.StatusCode = projection.StatusCode >= 400 ? projection.StatusCode : 500;
        return Task.FromResult(true);
    }
}

/// <summary>
/// TokenGateway 未捕获失败：固定 INTERNAL 形状，不含异常原文。
/// </summary>
[ExposeServices(typeof(IClientErrorProtocolAdapter))]
public class GatewayErrorProtocolAdapter : IClientErrorProtocolAdapter, ISingletonDependency
{
    public ErrorProtocol Protocol => ErrorProtocol.Gateway;

    public async Task<bool> TryWriteProtocolErrorAsync(
        HttpContext httpContext,
        ClientErrorHttpProjection projection,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Response.HasStarted)
        {
            return true;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = projection.StatusCode >= 400 ? projection.StatusCode : 500;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        ClientErrorHttpHeaders.ApplyRetryAfter(httpContext.Response, projection.RetryAfterSeconds);
        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "INTERNAL",
                message = projection.Body.Message,
            },
            cancellationToken);
        return true;
    }
}