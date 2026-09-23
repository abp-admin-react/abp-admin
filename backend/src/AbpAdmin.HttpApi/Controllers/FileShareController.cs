using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Content;

namespace AbpAdmin.Controllers;

/// <summary>
/// T4.6：分享链接匿名下载。应用服务负责校验 token、占用次数并按流读 blob；本控制器只钉死 GET。
/// 业务失败一律 404，不把 BusinessException 冒泡到开发异常页（会带堆栈，也便于枚举 token）。
/// 限流命中（AbpAdminOperationRateLimitingException，429）不在此吞掉，交给 ABP 异常处理返回 429 + Retry-After。
/// </summary>
[Route("api/app/file-share")]
public class FileShareController : AbpController
{
    private readonly Files.IFileShareAppService _fileShareAppService;

    public FileShareController(Files.IFileShareAppService fileShareAppService)
    {
        _fileShareAppService = fileShareAppService;
    }

    [HttpGet("by-token/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadByTokenAsync(string token)
    {
        try
        {
            var content = await _fileShareAppService.DownloadByTokenAsync(token);
            return File(
                content.GetStream(),
                content.ContentType ?? "application/octet-stream",
                content.FileName);
        }
        catch (BusinessException ex) when (ex is not AbpAdminOperationRateLimitingException)
        {
            return NotFound();
        }
    }
}
