using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 替换默认授权异常 handler：Challenge/Forbid 之后，受控入口若仍无协议体则补安全信封。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAbpAuthorizationExceptionHandler), typeof(DefaultAbpAuthorizationExceptionHandler))]
public class ClientErrorAuthorizationExceptionHandler : DefaultAbpAuthorizationExceptionHandler
{
    public override async Task HandleAsync(AbpAuthorizationException exception, HttpContext httpContext)
    {
        await base.HandleAsync(exception, httpContext);

        if (!ClientErrorRequestClassifier.IsControlled(httpContext) || httpContext.Response.HasStarted)
        {
            return;
        }

        var authenticated = httpContext.User.Identity?.IsAuthenticated == true;
        var status = httpContext.Response.StatusCode >= 400
            ? httpContext.Response.StatusCode
            : authenticated ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;

        var dispatcher = httpContext.RequestServices.GetRequiredService<ClientErrorHttpDispatcher>();
        await dispatcher.WriteKnownAsync(
            httpContext,
            authenticated ? ClientErrorCodes.Forbidden : ClientErrorCodes.AuthenticationRequired,
            status);
    }
}
