using System;
using AbpAdmin.Gdpr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc;

namespace AbpAdmin.Controllers;

/// <summary>
/// Cookie Consent 控制器。记录用户的接受状态。
///
/// 免责说明：Cookie Consent 功能只负责显示横幅并记录用户的接受状态，
/// 不会自动阻止任何非必要 cookie 或跟踪脚本。如果应用中集成了统计、广告
/// 或第三方跟踪脚本，需要开发者自己根据接受状态去控制这些脚本是否加载。
/// </summary>
[Route("api/app/cookie-consent")]
public class CookieConsentController : AbpController
{
    /// <summary>
    /// 接受状态 cookie 名。
    /// </summary>
    public const string ConsentCookieName = ".AbpAdmin.CookieConsent";

    private readonly AbpAdminCookieConsentOptions _options;

    public CookieConsentController(IOptions<AbpAdminCookieConsentOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 记录用户已接受 Cookie 策略。写入一个 cookie，Expiration 按配置。
    /// HttpOnly = false（前端要读），SameSite = Lax。
    /// </summary>
    [HttpPost("accept")]
    [AllowAnonymous]
    public IActionResult AcceptAsync()
    {
        Response.Cookies.Append(
            ConsentCookieName,
            "accepted",
            new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.Add(_options.Expiration),
                Path = "/"
            });

        return NoContent();
    }
}
