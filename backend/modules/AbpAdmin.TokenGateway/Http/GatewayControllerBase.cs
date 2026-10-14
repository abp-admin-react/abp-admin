using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.TokenGateway.Http;

// 自带 hb-/hbs- 鉴权，绕开宿主 FallbackPolicy（Bearer 不是 OIDC 令牌）。
[AllowAnonymous]
[ApiController]
[IgnoreAntiforgeryToken]
[ApiExplorerSettings(GroupName = "token-gateway")]
public abstract class GatewayControllerBase : ControllerBase
{
    protected string? Bearer()
    {
        var h = Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? h[7..].Trim() : null;
    }

    /// <summary>统一错误体：{ error: { code, message } }，不带任何上游信息。</summary>
    public static IActionResult Error(GatewayException e) =>
        new ObjectResult(new { error = new { code = e.Code, message = e.Message, type = e.Code } }) { StatusCode = e.Status };

    protected async Task<IActionResult> Run(Func<Task<IActionResult>> action, ILogger log)
    {
        try
        {
            return await action();
        }
        catch (GatewayException e)
        {
            return Error(e);
        }
        catch (Exception e) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            log.LogError(e, "token-gateway unhandled");
            return Error(GatewayException.Internal());
        }
    }
}
