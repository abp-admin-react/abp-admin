using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// HTTP 错误响应写入器实现。
/// 实现 IClientErrorResponseWriter 契约，一次性写出安全响应。
/// </summary>
public class ClientErrorResponseWriter : IClientErrorResponseWriter
{
    private readonly JsonSerializerOptions _jsonOptions;

    public ClientErrorResponseWriter()
    {
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
            return;
        }

        httpContext.Response.Clear();

        httpContext.Response.StatusCode = projection.StatusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        if (projection.RetryAfterSeconds.HasValue && projection.RetryAfterSeconds.Value >= 0)
        {
            var retryValue = projection.RetryAfterSeconds.Value;
            if (retryValue > 86400)
            {
                retryValue = 86400;
            }
            httpContext.Response.Headers["Retry-After"] = retryValue.ToString();
        }

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