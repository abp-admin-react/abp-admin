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
        var projection = projector.Project(new ClientErrorHttpProjectionContext
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
            context.HttpContext.Response.StatusCode = projection.StatusCode;
            context.HttpContext.Response.Headers.Append(AbpHttpConsts.AbpErrorFormat, "true");
            if (projection.RetryAfterSeconds is >= 0)
            {
                context.HttpContext.Response.Headers.RetryAfter = projection.RetryAfterSeconds.Value.ToString();
            }
        }

        context.Result = new ObjectResult(ToRemoteServiceErrorResponse(projection));
        context.ExceptionHandled = true;
    }

    internal static RemoteServiceErrorResponse ToRemoteServiceErrorResponse(ClientErrorHttpProjection projection)
    {
        var info = new RemoteServiceErrorInfo(
            projection.Body.Message,
            details: null,
            code: projection.Body.PublicCode);

        if (info.Data != null)
        {
            foreach (var pair in projection.Body.Data)
            {
                if (pair.Key != null)
                {
                    info.Data[pair.Key] = pair.Value;
                }
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
