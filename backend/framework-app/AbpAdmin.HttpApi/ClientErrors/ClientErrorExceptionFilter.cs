using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.ExceptionHandling;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 替换 AbpExceptionFilter：受控 API（含 void/FileResult）进入处理，写出投影后的新对象。
/// 日志仍使用原始诊断转换结果。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(AbpExceptionFilter))]
public class ClientErrorExceptionFilter : AbpExceptionFilter
{
    protected override bool ShouldHandleException(ExceptionContext context)
    {
        if (context.ExceptionHandled)
        {
            return false;
        }

        if (ClientErrorRequestClassifier.IsControlled(context.HttpContext))
        {
            return true;
        }

        return base.ShouldHandleException(context);
    }

    protected override async Task HandleAndWrapException(ExceptionContext context)
    {
        // 响应已开始：禁止再追加 JSON。标 handled 避免 MVC 再写第二份信封。
        // 中间件路径会 rethrow 以中止连接；MVC 下载可能留下截断 200，A11 只禁止与 error JSON 拼接。
        if (context.HttpContext.Response.HasStarted)
        {
            LogException(context, out _);
            context.ExceptionHandled = true;
            return;
        }

        LogException(context, out _);
        await context.GetRequiredService<IExceptionNotifier>()
            .NotifyAsync(new ExceptionNotificationContext(context.Exception));

        if (context.Exception is AbpAuthorizationException authorizationException)
        {
            await context.HttpContext.RequestServices
                .GetRequiredService<IAbpAuthorizationExceptionHandler>()
                .HandleAsync(authorizationException, context.HttpContext);
            if (context.HttpContext.Response.HasStarted)
            {
                context.ExceptionHandled = true;
                return;
            }
        }

        var projector = context.HttpContext.RequestServices.GetRequiredService<IClientErrorHttpProjector>();
        var projection = await projector.ProjectAsync(new ClientErrorHttpProjectionContext
        {
            HttpContext = context.HttpContext,
            Exception = context.Exception,
        });

        if (projection == null)
        {
            context.ExceptionHandled = true;
            return;
        }

        var registry = context.HttpContext.RequestServices.GetService<IControlledProtocolRegistry>();
        var match = registry?.FindMatch(
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value ?? string.Empty);
        if (match != null && match.Protocol != ErrorProtocol.AbpEnvelope)
        {
            var adapter = context.HttpContext.RequestServices
                .GetServices<IClientErrorProtocolAdapter>()
                .FirstOrDefault(a => a.Protocol == match.Protocol);
            if (adapter != null
                && await adapter.TryWriteProtocolErrorAsync(
                    context.HttpContext,
                    projection,
                    context.HttpContext.RequestAborted))
            {
                context.ExceptionHandled = true;
                return;
            }
        }

        if (!context.HttpContext.Response.HasStarted)
        {
            context.HttpContext.Response.Clear();
            ApplyEnvelopeHeaders(context.HttpContext, projection);
        }

        context.Result = new ObjectResult(ToRemoteServiceErrorResponse(projection));
        context.ExceptionHandled = true;
    }

    /// <summary>
    /// 写出前对齐状态码、ABP 错误格式头和封顶后的 Retry-After。
    /// </summary>
    /// <param name="httpContext">尚未开始写体的响应。</param>
    /// <param name="projection">公开投影；Retry-After 超过 86400 秒按一天封顶，拒绝无界倒计时。</param>
    /// <remarks>
    /// 必须先 <c>Clear()</c>：已写入的 Content-Disposition 等头不能跟着错误 JSON 走。
    /// Filter 与 <see cref="ClientErrorActionResults"/> 共用，避免 MVC 出口漏头。
    /// </remarks>
    internal static void ApplyEnvelopeHeaders(HttpContext httpContext, ClientErrorHttpProjection projection)
    {
        httpContext.Response.StatusCode = projection.StatusCode;
        if (!httpContext.Response.Headers.ContainsKey(AbpHttpConsts.AbpErrorFormat))
        {
            httpContext.Response.Headers.Append(AbpHttpConsts.AbpErrorFormat, "true");
        }

        ClientErrorHttpHeaders.ApplyRetryAfter(httpContext.Response, projection.RetryAfterSeconds);
    }

    /// <summary>
    /// 把投影转成 ABP <see cref="RemoteServiceErrorResponse"/>，供 MVC ObjectResult 序列化。
    /// </summary>
    /// <param name="projection">已过滤的公开投影；Data 只含批准键。</param>
    /// <returns>与 Writer 匿名信封字段对齐的 ABP 信封。</returns>
    /// <remarks>
    /// ABP 10.7 三参构造把 <c>RemoteServiceErrorInfo.Data</c> 留空。
    /// 必须先初始化字典再拷贝，否则 correlationId / retryAfterSeconds 不会出现在 MVC JSON。
    /// </remarks>
    public static RemoteServiceErrorResponse ToRemoteServiceErrorResponse(ClientErrorHttpProjection projection)
    {
        var info = new RemoteServiceErrorInfo(
            projection.Body.Message,
            details: null,
            code: projection.Body.PublicCode);

        info.Data ??= new Dictionary<string, object>();
        foreach (var pair in projection.Body.Data)
        {
            if (!string.IsNullOrEmpty(pair.Key) && pair.Value is not null)
            {
                info.Data[pair.Key] = pair.Value;
            }
        }

        if (projection.Body.ValidationErrors.Count > 0)
        {
            info.ValidationErrors = projection.Body.ValidationErrors
                .Select(item => new RemoteServiceValidationErrorInfo(item.Message, item.Members.ToArray()))
                .ToArray();
        }

        return new RemoteServiceErrorResponse(info);
    }
}
