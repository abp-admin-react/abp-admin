using System;
using System.Threading.Tasks;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.ExceptionHandling;

namespace AbpAdmin;

/// <summary>
/// 外层协议感知兜底：受控入口尚未被 MVC filter / UOW 异常中间件消费的异常。
/// </summary>
public class FriendlyBusinessExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FriendlyBusinessExceptionMiddleware> _logger;

    public FriendlyBusinessExceptionMiddleware(RequestDelegate next, ILogger<FriendlyBusinessExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted || !ClientErrorRequestClassifier.IsControlled(context))
            {
                throw;
            }

            var exceptionHandlingOptions = context.RequestServices
                .GetRequiredService<IOptions<AbpExceptionHandlingOptions>>().Value;
            if (exceptionHandlingOptions.ShouldLogException(ex))
            {
                _logger.LogException(ex);
            }

            await context.RequestServices
                .GetRequiredService<IExceptionNotifier>()
                .NotifyAsync(new ExceptionNotificationContext(ex));

            var dispatcher = context.RequestServices.GetRequiredService<ClientErrorHttpDispatcher>();
            await dispatcher.WriteAsync(context, ex);
        }
    }
}

public static class FriendlyBusinessExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseFriendlyBusinessException(this IApplicationBuilder app)
    {
        return app.UseMiddleware<FriendlyBusinessExceptionMiddleware>();
    }
}
