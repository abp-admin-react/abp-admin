using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Imaging;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
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

    /// <summary>
    /// 同步包装。HTTP 热路径必须走 <see cref="ProjectAsync"/>，避免资格解析阻塞线程池。
    /// </summary>
    public ClientErrorHttpProjection? Project(ClientErrorHttpProjectionContext context)
    {
        return ProjectAsync(context).GetAwaiter().GetResult();
    }

    public async Task<ClientErrorHttpProjection?> ProjectAsync(ClientErrorHttpProjectionContext context)
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
            return await ProjectCoreAsync(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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

    /// <summary>
    /// 组装投影输入：声明受众优先；仅 AdminCompatibility 走资格闸；文化取当前 UI 文化。
    /// </summary>
    /// <param name="context">HTTP 投影上下文。不得为 null。</param>
    /// <returns>安全信封模型。资格失败时受众降为 Public，不抛给客户端。</returns>
    /// <remarks>
    /// Public/Shared 不调用资格解析，避免每个客户错误都记 Admin degrade。
    /// 取消异常不在此捕获，由 <see cref="ProjectAsync"/> 的 filter 继续抛出。
    /// </remarks>
    private async Task<ClientErrorHttpProjection> ProjectCoreAsync(ClientErrorHttpProjectionContext context)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var registration = context.EndpointRegistration
                           ?? httpContext.GetEndpoint()?.Metadata.GetMetadata<ClientErrorEndpointMetadata>()
                               ?.ToRegistration();

        var declaredAudience = registration?.Audience ?? context.EffectiveAudience;
        var eligibilityResult = declaredAudience == ClientErrorAudience.AdminCompatibility
            ? await _eligibilityResolver.ResolveAsync(
                new AdminErrorEligibilityContext
                {
                    HttpContext = httpContext,
                    EndpointRegistration = registration,
                })
            : null;

        if (eligibilityResult is { IsEligible: false, DegradeReason: { } reason })
        {
            services?.GetService<IAdminEligibilityDegradeMetrics>()
                ?.Record(reason, registration?.ExitId ?? "unregistered");
        }

        var audience = declaredAudience == ClientErrorAudience.AdminCompatibility
            ? eligibilityResult is { IsEligible: true }
                ? ClientErrorAudience.AdminCompatibility
                : ClientErrorAudience.Public
            : declaredAudience;

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
            CultureName = CultureInfo.CurrentUICulture.Name,
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

    /// <summary>
    /// 决定 HTTP 状态码。目录 Kind 优先于 ABP 的 BusinessException 默认 403。
    /// </summary>
    /// <param name="context">可带显式状态（WriteKnown / ActionResults）。</param>
    /// <param name="kind">投影核心给出的公开分类。</param>
    /// <returns>400–599 状态码。</returns>
    /// <remarks>
    /// ABP <c>IHttpExceptionStatusCodeFinder</c> 把未标状态的 <c>BusinessException</c> 映射成 403。
    /// 若先信 finder，已登记的 <c>Client:NotFound</c> 会变成 Forbidden，登录恢复和 404 静默都会走错。
    /// 页面流 Kind（401/403/404/400/409/429）以目录为准；Server 才保留已写出的 503 等。
    /// </remarks>
    private static int DetermineHttpStatus(ClientErrorHttpProjectionContext context, PublicErrorKind kind)
    {
        if (context.HttpStatusCode is >= 400 and <= 599)
        {
            return context.HttpStatusCode.Value;
        }

        var fromKind = kind switch
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

        if (kind is not PublicErrorKind.Server)
        {
            return fromKind;
        }

        var currentStatus = context.HttpContext.Response.StatusCode;
        if (currentStatus >= 400)
        {
            return currentStatus;
        }

        if (context.Exception is IHasHttpStatusCode hasStatus
            && hasStatus.HttpStatusCode is >= 400 and <= 599)
        {
            return hasStatus.HttpStatusCode;
        }

        return fromKind;
    }
}
