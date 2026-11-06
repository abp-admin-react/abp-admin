using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 端点授权短路由：默认 Challenge/Forbid 之后，受控入口补安全信封。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAuthorizationMiddlewareResultHandler))]
public class ClientErrorAuthorizationMiddlewareResultHandler
    : IAuthorizationMiddlewareResultHandler, ITransientDependency
{
    private readonly AuthorizationMiddlewareResultHandler _inner = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        await _inner.HandleAsync(next, context, policy, authorizeResult);

        if (authorizeResult.Succeeded
            || !ClientErrorRequestClassifier.IsControlled(context)
            || context.Response.HasStarted)
        {
            return;
        }

        var authenticated = context.User.Identity?.IsAuthenticated == true;
        var status = context.Response.StatusCode >= 400
            ? context.Response.StatusCode
            : authenticated ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;

        var dispatcher = context.RequestServices.GetRequiredService<ClientErrorHttpDispatcher>();
        await dispatcher.WriteKnownAsync(
            context,
            authenticated ? ClientErrorCodes.Forbidden : ClientErrorCodes.AuthenticationRequired,
            status);
    }
}
