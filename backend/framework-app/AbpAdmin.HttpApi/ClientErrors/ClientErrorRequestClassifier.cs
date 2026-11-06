using Microsoft.AspNetCore.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 判断请求是否属于受控 API/协议入口（相对 Razor 页面）。
/// </summary>
public static class ClientErrorRequestClassifier
{
    public static bool IsControlled(HttpContext httpContext)
    {
        var path = httpContext.Request.Path;
        return path.StartsWithSegments("/api")
               || path.StartsWithSegments("/connect")
               || path.StartsWithSegments("/v1")
               || path.StartsWithSegments("/signalr-hubs");
    }
}
