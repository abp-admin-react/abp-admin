using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 中间件路径：按协议适配器或 ABP writer 写一次安全响应。
/// 响应已开始则 no-op 并打 Warning，与 Filter / Writer 同一口径。
/// </summary>
public class ClientErrorHttpDispatcher : ITransientDependency
{
    private readonly ILogger<ClientErrorHttpDispatcher> _logger;

    public ClientErrorHttpDispatcher(ILogger<ClientErrorHttpDispatcher>? logger = null)
    {
        _logger = logger ?? NullLogger<ClientErrorHttpDispatcher>.Instance;
    }

    public Task WriteAsync(HttpContext httpContext, Exception? exception)
    {
        return WriteCoreAsync(httpContext, new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = exception,
        });
    }

    /// <summary>
    /// 已知内部码的手写出口。协议默认走路径登记表。
    /// </summary>
    /// <param name="httpContext">当前请求。</param>
    /// <param name="internalCode">公开目录内部码；null 时按状态码投影。</param>
    /// <param name="statusCode">已确定的 HTTP 状态。</param>
    /// <param name="exceptionData">受控参数快照；只投影批准键。</param>
    /// <param name="retryAfterSeconds">等待秒数；Clear 后由适配器/Writer 封顶写出。</param>
    /// <param name="protocolOverride">仅当调用方已处于该协议路径时传入；生产 /api 不得压成 OAuth。</param>
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
            _logger.LogWarning(
                "错误响应已开始，中间件路径不再追加 JSON。Path={Path}",
                httpContext.Request.Path.Value);
            return;
        }

        var projector = httpContext.RequestServices.GetRequiredService<IClientErrorHttpProjector>();
        var projection = await projector.ProjectAsync(projectionContext);
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
