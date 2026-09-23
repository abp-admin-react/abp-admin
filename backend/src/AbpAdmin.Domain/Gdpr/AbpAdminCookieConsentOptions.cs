using System;

namespace AbpAdmin.Gdpr;

/// <summary>
/// Cookie Consent 配置选项。
/// 从 appsettings.json 的 "CookieConsent" 节绑定。
///
/// 免责说明：Cookie Consent 功能只负责显示横幅并记录用户的接受状态，
/// 不会自动阻止任何非必要 cookie 或跟踪脚本。如果应用中集成了统计、广告
/// 或第三方跟踪脚本，需要开发者自己根据接受状态去控制这些脚本是否加载。
/// </summary>
public class AbpAdminCookieConsentOptions
{
    public const string SectionName = "CookieConsent";

    /// <summary>
    /// 是否启用 Cookie Consent 横幅。默认 false。
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Cookie 策略链接。
    /// </summary>
    public string? CookiePolicyUrl { get; set; }

    /// <summary>
    /// 隐私策略链接。
    /// </summary>
    public string? PrivacyPolicyUrl { get; set; }

    /// <summary>
    /// 接受状态 cookie 的有效期。默认 180 天（6 个月）。
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromDays(180);
}
