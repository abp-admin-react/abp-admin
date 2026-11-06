using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 中间件路径：按协议适配器或 ABP writer 写一次安全响应。
/// </summary>
public class ClientErrorHttpDispatcher : ITransientDependency
{
    public Task WriteAsync(HttpContext httpContext, Exception? exception)
    {
        return WriteCoreAsync(httpContext, new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = exception,
        });
    }

    public Task WriteKnownAsync(
        HttpContext httpContext,
        string? internalCode,
        int statusCode,
        IReadOnlyDictionary<string, object?>? exceptionData = null,
        int? retryAfterSeconds = null,
        ErrorProtocol? protocolOverride = null)
    {
        return WriteCoreAsync(httpContext, new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            InternalCode = internalCode,
            HttpStatusCode = statusCode,
            ExceptionData = exceptionData,
            RetryAfterSeconds = retryAfterSeconds,
            ProtocolOverride = protocolOverride,
        });
    }

    private async Task WriteCoreAsync(HttpContext httpContext, ClientErrorHttpProjectionContext projectionContext)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var projector = httpContext.RequestServices.GetRequiredService<IClientErrorHttpProjector>();
        var projection = projector.Project(projectionContext);
        if (projection == null)
        {
            return;
        }

        var protocol = projectionContext.ProtocolOverride
                       ?? httpContext.RequestServices.GetService<IControlledProtocolRegistry>()
                           ?.FindMatch(httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty)
                           ?.Protocol;

        if (protocol is { } selected && selected != ErrorProtocol.AbpEnvelope)
        {
            var adapter = httpContext.RequestServices
                .GetServices<IClientErrorProtocolAdapter>()
                .FirstOrDefault(a => a.Protocol == selected);
            if (adapter != null
                && await adapter.TryWriteProtocolErrorAsync(
                    httpContext,
                    projection,
                    httpContext.RequestAborted))
            {
                return;
            }
        }

        if (!httpContext.Response.Headers.ContainsKey(AbpHttpConsts.AbpErrorFormat))
        {
            httpContext.Response.Headers.Append(AbpHttpConsts.AbpErrorFormat, "true");
        }

        var writer = httpContext.RequestServices.GetRequiredService<IClientErrorResponseWriter>();
        await writer.WriteAsync(httpContext, projection, httpContext.RequestAborted);
    }
}
