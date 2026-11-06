using System;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Gdpr;
using AbpAdmin.HttpApi.ClientErrors;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
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
/// 消费 [OperationRateLimiting]（已删除从未注册进 MvcOptions 的死 Filter
/// OperationRateLimitingActionFilter），Controller 级特性无效；且双落点会对同一
/// 请求计两次数。
///
/// 异常出口：业务校验失败（无效 token / 数据未就绪）在此折叠为公开 404 信封，
/// 与 FileShare 相同，避免匿名端区分 InvalidDownloadToken 与 DataNotReady。
/// 限流（AbpAdminOperationRateLimitingException）不吞，交给统一异常处理返回 429。
/// </summary>
[Route("api/app/gdpr-request")]
[ClientErrorAudience(ClientErrorAudience.Public)]
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
    /// 业务校验失败（无效 token / 数据未就绪）→ 公开 404 信封，不区分原因。
    /// </summary>
    [HttpGet("download")]
    [AllowAnonymous]
    [DisableAuditing]
    public async Task<IActionResult> DownloadAsync([FromQuery] Guid requestId, [FromQuery] string token)
    {
        try
        {
            var content = await _gdprRequestAppService.DownloadAsync(requestId, token);
            return File(
                content.GetStream(),
                content.ContentType ?? "application/zip",
                content.FileName);
        }
        catch (BusinessException ex) when (ex is not AbpAdminOperationRateLimitingException)
        {
            return await ClientErrorActionResults.NotFoundAsync(HttpContext);
        }
    }
}
