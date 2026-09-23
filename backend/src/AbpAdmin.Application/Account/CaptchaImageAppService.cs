using System;
using System.Threading.Tasks;
using AbpAdmin.Captcha;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Account;

/// <summary>
/// 自托管图形验证码：匿名取图端点（登录页用）。
/// 返回 data URL 而非文件流：登录页是 Razor 渲染，img 直接塞 data URL 最省事，
/// 也避免为二进制内容协商多开一条匿名路由。
/// 注意：不做"是否启用"校验——生成一张图无害，缓存 TTL 5 分钟自然回收；
/// 校验端（LoginCaptchaManager）才决定是否强制。
/// round4：补按 IP 的操作限流（CaptchaImage 策略）——此前匿名无限刷 Skia 渲染与缓存写入。
/// </summary>
[AllowAnonymous]
public class CaptchaImageAppService : ApplicationService
{
    private readonly CaptchaImageGenerator _generator;
    private readonly ImageCaptchaManager _imageCaptchaManager;

    public CaptchaImageAppService(
        CaptchaImageGenerator generator,
        ImageCaptchaManager imageCaptchaManager)
    {
        _generator = generator;
        _imageCaptchaManager = imageCaptchaManager;
    }

    /// <summary>ABP 动态 API 路由：GET /api/app/captcha-image</summary>
    [OperationRateLimiting(OperationRateLimitingPolicyNames.CaptchaImage)]
    public virtual async Task<CaptchaImageDto> GetAsync()
    {
        var id = GuidGenerator.Create();
        var (code, png) = _generator.Generate();
        await _imageCaptchaManager.RememberAsync(id, code);

        return new CaptchaImageDto
        {
            Id = id,
            ImageDataUrl = $"data:image/png;base64,{Convert.ToBase64String(png)}"
        };
    }
}
