using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 生产者安全 ActionResult helper。空体 Forbid/NotFound 不会被授权异常 handler 覆盖。
/// </summary>
public static class ClientErrorActionResults
{
    /// <summary>客户可见 404：写出公开目录 NotFound 信封。</summary>
    public static async Task<IActionResult> NotFoundAsync(HttpContext httpContext)
    {
        return await CreateAsync(httpContext, ClientErrorCodes.NotFound, StatusCodes.Status404NotFound);
    }

    /// <summary>客户可见 403：写出公开目录 Forbidden 信封。</summary>
    public static async Task<IActionResult> ForbiddenAsync(HttpContext httpContext)
    {
        return await CreateAsync(httpContext, ClientErrorCodes.Forbidden, StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// 管理导出等隐藏资源路径：保持空体 404，不写内部原因。
    /// </summary>
    public static IActionResult EmptyNotFound() => new NotFoundResult();

    /// <summary>
    /// 管理导出等跨用户路径：保持空体 403。
    /// </summary>
    public static IActionResult EmptyForbidden() => new ForbidResult();

    /// <summary>
    /// 把投影变成 ObjectResult。投影为 null（响应已开始）时只回 500 空体，不再追加 JSON。
    /// </summary>
    public static IActionResult FromProjection(ClientErrorHttpProjection? projection)
    {
        if (projection == null)
        {
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new ObjectResult(ClientErrorExceptionFilter.ToRemoteServiceErrorResponse(projection))
        {
            StatusCode = projection.StatusCode,
            ContentTypes = { "application/json" },
        };
    }

    private static async Task<IActionResult> CreateAsync(
        HttpContext httpContext,
        string internalCode,
        int statusCode)
    {
        var projector = httpContext.RequestServices.GetRequiredService<IClientErrorHttpProjector>();
        var projection = await projector.ProjectAsync(new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            InternalCode = internalCode,
            HttpStatusCode = statusCode,
        });

        if (!httpContext.Response.HasStarted)
        {
            // 失败路径不得带着成功响应的 Content-Disposition 等头出门。
            httpContext.Response.Clear();
            if (projection != null)
            {
                ClientErrorExceptionFilter.ApplyEnvelopeHeaders(httpContext, projection);
            }
        }

        return FromProjection(projection);
    }
}
