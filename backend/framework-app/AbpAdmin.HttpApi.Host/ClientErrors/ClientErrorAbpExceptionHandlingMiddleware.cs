using System;
using System.Threading.Tasks;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ExceptionHandling;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 替换 UseUnitOfWork 隐式安装的 AbpExceptionHandlingMiddleware。
/// 受控入口投影后写一次；非 API 页面委托原行为。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(AbpExceptionHandlingMiddleware))]
public class ClientErrorAbpExceptionHandlingMiddleware : AbpExceptionHandlingMiddleware
{
    private readonly ILogger<ClientErrorAbpExceptionHandlingMiddleware> _typedLogger;

    public ClientErrorAbpExceptionHandlingMiddleware(
        ILogger<AbpExceptionHandlingMiddleware> logger,
        ILogger<ClientErrorAbpExceptionHandlingMiddleware> typedLogger)
        : base(logger)
    {
        _typedLogger = typedLogger;
    }

    public override async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _typedLogger.LogWarning(ex, "Exception after response started; not appending JSON.");
                throw;
            }

            if (!ClientErrorRequestClassifier.IsControlled(context))
            {
                await base.InvokeAsync(context, _ => throw ex);
                return;
            }

            await HandleControlledAsync(context, ex);
        }
    }

    private async Task HandleControlledAsync(HttpContext context, Exception exception)
    {
        var exceptionHandlingOptions = context.RequestServices
            .GetRequiredService<IOptions<AbpExceptionHandlingOptions>>().Value;
        if (exceptionHandlingOptions.ShouldLogException(exception))
        {
            _typedLogger.LogException(exception);
        }

        await context.RequestServices
            .GetRequiredService<IExceptionNotifier>()
            .NotifyAsync(new ExceptionNotificationContext(exception));

        if (exception is AbpAuthorizationException authorizationException)
        {
            await context.RequestServices
                .GetRequiredService<IAbpAuthorizationExceptionHandler>()
                .HandleAsync(authorizationException, context);
            if (context.Response.HasStarted)
            {
                return;
            }
        }

        var dispatcher = context.RequestServices.GetRequiredService<ClientErrorHttpDispatcher>();
        await dispatcher.WriteAsync(context, exception);
    }
}
