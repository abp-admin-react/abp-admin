using System;
using System.Threading.Tasks;
using AbpAdmin.Gdpr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Auditing;

namespace AbpAdmin.Controllers;

/// <summary>
/// GDPR 下载控制器。
/// 应用服务只负责校验 token 与打包，HTTP 动词由此 Controller 钉死为 GET，
/// 避免动态 API 把 DownloadAsync 映射成 POST。
///
/// 匿名端点限流（GdprDownload 策略，按 IP 10 次/分钟）：特性标在
/// GdprRequestAppService.DownloadAsync 上、经 DI 拦截器生效——与 FileShare 下载
/// 同一落点（FileShareController 亦未在 Controller 上标注）。MVC 侧没有 Filter
/// 消费 [OperationRateLimiting]（round3 已删除从未注册进 MvcOptions 的死 Filter
/// OperationRateLimitingActionFilter），Controller 级特性无效；且双落点会对同一
/// 请求计两次数。
///
/// 异常出口（R3-D12 后的职责划分，勿在本类加 catch）：
/// - 限流命中（AbpAdminOperationRateLimitingException，IHasHttpStatusCode=429）与
///   业务校验失败（AbpAdmin:Gdpr:InvalidDownloadToken / DataNotReady 等
///   BusinessException）都由 FriendlyBusinessExceptionMiddleware 统一转译为
///   「接口状态码 + ABP 错误格式 JSON」——该中间件注册在开发者异常页之后（更内层），
///   先于本类收到异常；无码异常仍冒泡给开发者异常页/错误页。
/// - 本类保持无任何 try/catch：DownloadAsync 返回 FileResult（非 ObjectResult），
///   官方 MVC 异常过滤器不覆盖本动作，兜底职责全在上诉中间件（见其头注释）。
/// </summary>
[Route("api/app/gdpr-request")]
public class GdprDownloadController : AbpController
{
    private readonly IGdprRequestAppService _gdprRequestAppService;

    public GdprDownloadController(IGdprRequestAppService gdprRequestAppService)
    {
        _gdprRequestAppService = gdprRequestAppService;
    }

    /// <summary>
    /// 匿名下载端点，token 即凭据。[DisableAuditing]：token 是一次性安全凭据，
    /// 不能进审计参数（应用服务侧同注解 + 方法内脱敏日志）。
    /// 业务校验失败（无效 token / 数据未就绪）→ 403 + 错误码，由上述中间件转译。
    /// </summary>
    [HttpGet("download")]
    [AllowAnonymous]
    [DisableAuditing]
    public async Task<IActionResult> DownloadAsync([FromQuery] Guid requestId, [FromQuery] string token)
    {
        var content = await _gdprRequestAppService.DownloadAsync(requestId, token);

        return File(
            content.GetStream(),
            content.ContentType ?? "application/zip",
            content.FileName);
    }
}
