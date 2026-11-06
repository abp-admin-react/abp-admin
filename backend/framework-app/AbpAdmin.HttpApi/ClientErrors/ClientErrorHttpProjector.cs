using System;
using System.Collections.Generic;
using AbpAdmin.ClientErrors;
using AbpAdmin.Imaging;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Tracing;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// HTTP 层组装投影输入并确定状态码。不修改原异常。
/// </summary>
public class ClientErrorHttpProjector : IClientErrorHttpProjector, ITransientDependency
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

        try
        {
            return ProjectCore(context);
        }
        catch
        {
            return new ClientErrorHttpProjection
            {
                Body = new PublicErrorProjectionResult
                {
                    PublicCode = ClientErrorCodes.Unexpected,
                    Kind = PublicErrorKind.Server,
                    Message = ClientErrorCodes.SafeFallbackMessage,
                },
                StatusCode = StatusCodes.Status500InternalServerError,
            };
        }
    }

    private ClientErrorHttpProjection ProjectCore(ClientErrorHttpProjectionContext context)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var registration = context.EndpointRegistration;

        var eligibilityResult = _eligibilityResolver.Resolve(
            new AdminErrorEligibilityContext
            {
                HttpContext = httpContext,
                EndpointRegistration = registration,
            });

        if (!eligibilityResult.IsEligible && eligibilityResult.DegradeReason is { } reason)
        {
            services?.GetService<IAdminEligibilityDegradeMetrics>()
                ?.Record(reason, registration?.ExitId ?? "unregistered");
        }

        var audience = eligibilityResult.IsEligible
            ? ClientErrorAudience.AdminCompatibility
            : context.EffectiveAudience == ClientErrorAudience.AdminCompatibility
                ? ClientErrorAudience.Public
                : context.EffectiveAudience;

        string? internalCode = context.InternalCode;
        IReadOnlyDictionary<string, object?>? exceptionData = context.ExceptionData;
        int? retryAfter = context.RetryAfterSeconds;

        if (context.Exception != null)
        {
            exceptionData ??= ExtractExceptionData(context.Exception);
            if (context.Exception is AbpAdminOperationRateLimitingException)
            {
                internalCode ??= ClientErrorCodes.RateLimited;
                retryAfter ??= TryReadRetryAfter(exceptionData);
            }
            else if (context.Exception is AbpAdminImageProcessingBusyException busy)
            {
                internalCode ??= busy.Code;
                retryAfter ??= busy.RetryAfterSeconds;
            }
            else
            {
                internalCode ??= (context.Exception as IHasErrorCode)?.Code;
            }
        }

        var correlationId = services?.GetService<ICorrelationIdProvider>()?.Get();

        var input = new PublicErrorProjectionInput
        {
            InternalCode = internalCode,
            ExceptionData = exceptionData,
            HttpStatusCode = context.HttpStatusCode ?? (httpContext.Response.StatusCode >= 400
                ? httpContext.Response.StatusCode
                : null),
            CorrelationId = correlationId,
            Audience = audience,
            EndpointRegistration = registration,
            RetryAfterSeconds = retryAfter,
        };

        var projectionResult = _projectionCore.Project(input);
        var statusCode = DetermineHttpStatus(context, projectionResult.Kind);

        return new ClientErrorHttpProjection
        {
            Body = projectionResult,
            StatusCode = statusCode,
            RetryAfterSeconds = projectionResult.RetryAfterSeconds ?? retryAfter,
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

    private static int? TryReadRetryAfter(IReadOnlyDictionary<string, object?>? exceptionData)
    {
        if (exceptionData == null
            || !exceptionData.TryGetValue("RetryAfterSeconds", out var raw)
            || raw == null)
        {
            return null;
        }

        return raw switch
        {
            int i when i >= 0 => i,
            long l when l >= 0 && l <= int.MaxValue => (int)l,
            double d when !double.IsNaN(d) && d >= 0 => (int)Math.Ceiling(d),
            float f when !float.IsNaN(f) && f >= 0 => (int)Math.Ceiling(f),
            string text when double.TryParse(text, out var parsed) && parsed >= 0
                => (int)Math.Ceiling(parsed),
            _ => null,
        };
    }

    private static int DetermineHttpStatus(ClientErrorHttpProjectionContext context, PublicErrorKind kind)
    {
        if (context.HttpStatusCode is >= 400 and <= 599)
        {
            return context.HttpStatusCode.Value;
        }

        var currentStatus = context.HttpContext.Response.StatusCode;
        if (currentStatus >= 400)
        {
            return currentStatus;
        }

        if (context.Exception != null)
        {
            var finder = context.HttpContext.RequestServices?.GetService<IHttpExceptionStatusCodeFinder>();
            if (finder != null)
            {
                return (int)finder.GetStatusCode(context.HttpContext, context.Exception);
            }

            if (context.Exception is IHasHttpStatusCode hasStatus)
            {
                return hasStatus.HttpStatusCode;
            }
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
}
