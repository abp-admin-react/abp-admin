using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// HTTP 错误投影器实现。
/// 实现 IClientErrorHttpProjector 契约，将 HTTP 上下文投影为安全响应。
/// </summary>
public class ClientErrorHttpProjector : IClientErrorHttpProjector
{
    private readonly IClientErrorProjectionCore _projectionCore;
    private readonly IAdminErrorEligibilityResolver _eligibilityResolver;

    public ClientErrorHttpProjector(
        IClientErrorProjectionCore projectionCore,
        IAdminErrorEligibilityResolver eligibilityResolver)
    {
        _projectionCore = projectionCore;
        _eligibilityResolver = eligibilityResolver;
    }

    public ClientErrorHttpProjection? Project(ClientErrorHttpProjectionContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.HasResponseStarted)
        {
            return null;
        }

        var eligibilityResult = _eligibilityResolver.Resolve(
            new AdminErrorEligibilityContext
            {
                HttpContext = context.HttpContext,
                EndpointRegistration = context.EndpointRegistration,
            });

        var audience = eligibilityResult.IsEligible
            ? ClientErrorAudience.AdminCompatibility
            : context.EffectiveAudience;

        string? internalCode = null;
        IReadOnlyDictionary<string, object?>? exceptionData = null;

        if (context.Exception != null)
        {
            internalCode = GetExceptionCode(context.Exception);
            exceptionData = ExtractExceptionData(context.Exception);
        }

        var correlationId = context.HttpContext.TraceIdentifier;

        var input = new PublicErrorProjectionInput
        {
            InternalCode = internalCode,
            ExceptionData = exceptionData,
            HttpStatusCode = context.HttpContext.Response.StatusCode,
            CorrelationId = correlationId,
            Audience = audience,
            EndpointRegistration = context.EndpointRegistration,
        };

        var projectionResult = _projectionCore.Project(input);

        var statusCode = DetermineHttpStatus(
            context.HttpContext.Response.StatusCode,
            projectionResult.Kind,
            context.Exception);

        var retryAfter = DetermineRetryAfter(projectionResult);

        return new ClientErrorHttpProjection
        {
            Body = projectionResult,
            StatusCode = statusCode,
            RetryAfterSeconds = retryAfter,
        };
    }

    private static IReadOnlyDictionary<string, object?>? ExtractExceptionData(Exception ex)
    {
        if (ex.Data == null || ex.Data.Count == 0)
        {
            return null;
        }

        var data = new Dictionary<string, object?>();
        foreach (var key in ex.Data.Keys)
        {
            if (key is string keyStr)
            {
                data[keyStr] = ex.Data[key];
            }
        }
        return data.Count > 0 ? data : null;
    }

    private static string? GetExceptionCode(Exception ex)
    {
        var codeProperty = ex.GetType().GetProperty("Code");
        return codeProperty?.GetValue(ex) as string;
    }

    private static int DetermineHttpStatus(int currentStatus, PublicErrorKind kind, Exception? ex)
    {
        if (currentStatus >= 400)
        {
            return currentStatus;
        }

        if (ex is IHasHttpStatusCode hasStatus)
        {
            return hasStatus.HttpStatusCode;
        }

        return kind switch
        {
            PublicErrorKind.Authentication => 401,
            PublicErrorKind.Forbidden => 403,
            PublicErrorKind.Validation => 400,
            PublicErrorKind.NotFound => 404,
            PublicErrorKind.RateLimited => 429,
            PublicErrorKind.Conflict => 409,
            PublicErrorKind.Server => 500,
            _ => 400,
        };
    }

    private static int? DetermineRetryAfter(PublicErrorProjectionResult result)
    {
        if (result.RetryAfterSeconds.HasValue && result.RetryAfterSeconds.Value >= 0)
        {
            return result.RetryAfterSeconds.Value;
        }
        return null;
    }
}

/// <summary>
/// 异常接口，用于获取 HTTP 状态码。
/// </summary>
public interface IHasHttpStatusCode
{
    int HttpStatusCode { get; }
}