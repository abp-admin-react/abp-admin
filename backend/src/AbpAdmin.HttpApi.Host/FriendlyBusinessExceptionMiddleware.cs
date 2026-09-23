using System;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Localization;

namespace AbpAdmin;

/// <summary>
/// 带错误码 BusinessException 的兜底转译（GUI 全链路测试 R3-D12）。
///
/// 【根因（已对照官方源码与文档查明）】ABP 的 AbpExceptionFilter 与
/// AbpExceptionHandlingMiddleware 只转译满足以下任一条件的异常：
/// ① Action 返回 ObjectResult；② 请求为 AJAX（X-Requested-With: XMLHttpRequest）；
/// ③ Accept 头显式包含 application/json（见 abp.io docs「Exception Handling」与
/// framework/src/Volo.Abp.AspNetCore.Mvc/.../AbpExceptionFilter.cs 的 ShouldHandleException）。
/// void 动作（GDPR 删户 DELETE）与返回 FileResult 的动作（下载）被官方设计为「放行」，
/// 异常一路冒泡到 DeveloperExceptionPage（Development）/ErrorPage（Production），
/// 以 500 + 堆栈/源码路径裸返回——匿名端点上属于信息泄露。
///
/// 【管道位置】必须注册在 UseDeveloperExceptionPage/UseErrorPage **之后**（管道上更内层）：
/// 异常沿管道由内向外 unwind，谁在更内层谁先拿到；同时需在 UseRouting 之前以包住端点。
/// 放在外层永远轮不到（本轮排障实测踩坑，见 docs/TEST_EVIDENCE_2026-09-21.md §6.5/§7.2）。
///
/// 【转译规则】与官方默认状态码映射保持一致（IHttpExceptionStatusCodeFinder 语义）：
/// - IHasHttpStatusCode（操作限流 429）：按接口给出的状态码返回；
/// - 其余带错误码 BusinessException：403 + ABP 错误格式 JSON
///   {error:{code,message,details,data,validationErrors}}，message 走本地化资源
///   （AbpAdmin 命名空间已在 AbpAdminDomainSharedModule.MapCodeNamespace 注册）；
/// - 无错误码的业务异常视为内部错误（与官方「无码 → 500」语义一致）、非业务异常、
///   以及响应已开始的场景：一律原样抛回，不在本层吞掉。
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
            // 与头注释「转译规则」严格对应：只接管带错误码的业务异常，
            // 其余（无码业务异常/框架异常/响应已开始）原样抛回原管道处理。
            if (ex is not BusinessException business
                || string.IsNullOrEmpty(business.Code)
                || context.Response.HasStarted)
            {
                throw;
            }

            var status = StatusCodes.Status403Forbidden;
            if (business is IHasHttpStatusCode hasStatus)
            {
                status = hasStatus.HttpStatusCode;
            }

            var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<AbpAdminResource>>();
            var message = localizer[business.Code].Value;

            // 转译动作必须留痕：状态码 + 错误码 + 路径，便于与前端上报对照
            _logger.LogWarning(
                "已兜底转译带码业务异常：{Code} → HTTP {StatusCode}，路径 {Path}",
                business.Code, status, context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = business.Code,
                    message,
                    details = (string?)null,
                    data = business.Data,
                    validationErrors = Array.Empty<object>(),
                },
            });
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
