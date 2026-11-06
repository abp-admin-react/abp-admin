using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// HTTP 错误响应写入器实现。
/// 一次性写出安全响应：Clear 后写状态码、Retry-After、ABP 信封。
/// 响应已开始则 no-op 并打 Warning，不追加 JSON。
/// 经 ABP 约定式注册为单例（无状态；接口名与类名符合 I{ClassName} 约定，默认暴露）。
/// </summary>
public class ClientErrorResponseWriter : IClientErrorResponseWriter, ISingletonDependency
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<ClientErrorResponseWriter> _logger;

    public ClientErrorResponseWriter(ILogger<ClientErrorResponseWriter>? logger = null)
    {
        _logger = logger ?? NullLogger<ClientErrorResponseWriter>.Instance;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task WriteAsync(
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
            _logger.LogWarning(
                "错误响应已开始，不再追加 JSON。Status={StatusCode} PublicCode={PublicCode}",
                projection.StatusCode,
                projection.Body.PublicCode);
            return;
        }

        httpContext.Response.Clear();

        httpContext.Response.StatusCode = projection.StatusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        ClientErrorHttpHeaders.ApplyRetryAfter(httpContext.Response, projection.RetryAfterSeconds);

        var envelope = new
        {
            error = new
            {
                code = projection.Body.PublicCode,
                message = projection.Body.Message,
                details = (string?)null,
                data = projection.Body.Data,
                validationErrors = projection.Body.ValidationErrors?.Select(ve => new
                {
                    message = ve.Message,
                    members = ve.Members,
                }).ToArray() ?? Array.Empty<object>(),
            },
        };

        await httpContext.Response.WriteAsJsonAsync(envelope, _jsonOptions, cancellationToken);
    }
}