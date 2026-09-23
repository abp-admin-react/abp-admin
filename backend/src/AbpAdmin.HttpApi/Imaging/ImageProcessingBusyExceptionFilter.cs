using AbpAdmin.Imaging;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AbpAdmin.Imaging;

/// <summary>
/// 给 <see cref="AbpAdminImageProcessingBusyException"/>（HTTP 503）补 Retry-After 响应头（T3.1）。
///
/// 落地方式说明：本过滤器不接管异常（ExceptionHandled 保持 false），只把头写进 Response，
/// 响应体仍由 ABP 的 AbpExceptionFilter 统一转换为标准 error JSON。
/// ASP.NET Core 的异常过滤器按「离 action 最近者先执行」的顺序运行，
/// 全局注册的本过滤器先于 ABP 框架注册的 AbpExceptionFilter 拿到异常。
/// </summary>
public class ImageProcessingBusyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is AbpAdminImageProcessingBusyException exception)
        {
            context.HttpContext.Response.Headers.RetryAfter = exception.RetryAfterSeconds.ToString();
        }
    }
}
