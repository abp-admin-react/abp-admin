using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Captcha;
using AbpAdmin.Localization;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Settings;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// 替换开源包登录页。T2.7 双因素跳转与外部登录过滤；T4.3 Passkey；T4.4 验证码（Turnstile / 自托管图形，见 LoginCaptchaManager）。
/// </summary>
public class AbpAdminLoginModel : OpenIddictSupportedLoginModel, IAccountCaptchaPageModel
{
    [BindProperty(Name = TurnstileCaptchaValidator.FormFieldName)]
    public string? TurnstileToken { get; set; }

    /// <summary>图形验证码（Image provider）：图片标识与用户输入。</summary>
    [BindProperty]
    public Guid? CaptchaId { get; set; }

    [BindProperty]
    public string? CaptchaCode { get; set; }

    [BindProperty]
    public string? PasskeyCredentialJson { get; set; }

    public bool CaptchaEnabled { get; private set; }

    /// <summary>当前生效的验证码提供者：Turnstile | Image | null（未启用）。</summary>
    public string? CaptchaProvider { get; set; }

    public string? CaptchaSiteKey { get; set; }

    private readonly LoginCaptchaManager _captchaManager;
    private readonly IStringLocalizer<AbpAdminResource> _abpAdminLocalizer;
    private readonly IOperationRateLimitingChecker _rateLimitingChecker;

    public AbpAdminLoginModel(
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IOptions<IdentityOptions> identityOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        AbpOpenIddictRequestHelper openIddictRequestHelper,
        IWebHostEnvironment webHostEnvironment,
        LoginCaptchaManager captchaManager,
        IStringLocalizer<AbpAdminResource> abpAdminLocalizer,
        IOperationRateLimitingChecker rateLimitingChecker)
        : base(schemeProvider, accountOptions, identityOptions,
            identityDynamicClaimsPrincipalContributorCache, openIddictRequestHelper, webHostEnvironment)
    {
        _captchaManager = captchaManager;
        _abpAdminLocalizer = abpAdminLocalizer;
        _rateLimitingChecker = rateLimitingChecker;
    }

    public override async Task<IActionResult> OnGetAsync()
    {
        await LoadCaptchaAsync();
        return await base.OnGetAsync();
    }

    public override async Task<IActionResult> OnPostAsync(string action)
    {
        await LoadCaptchaAsync();
        if (action == "Login")
        {
            // round3 接线 Login 限流策略（此前注册后全库零消费者，密码登录无应用层防爆破）：
            // 策略含 IP + 邮箱双规则，Checker 会全查——IP 规则防跨账户 password spraying
            // （Identity 只按账户锁定防不了），邮箱规则防单账户换 IP 打点。
            // 参数传用户输入的 userNameOrEmail（邮箱规则按参数分区、大小写归一化）。
            // 放在验证码校验之前：验证码可被关掉，限流是最后防线；验证码失败的尝试也计入窗口
            // （保守口径）。成功/失败后均不 Reset——固定窗口计数保持，防爆破语义优先。
            // 超限：API 路径由 ABP 异常处理返回 429 + Retry-After；登录页是 Razor Page，
            // 异常冒泡会落 500/开发者异常页而非友好提示，故在此转为页面告警（计数不 Reset，语义不变）。
            try
            {
                await _rateLimitingChecker.CheckAsync(
                    OperationRateLimitingPolicyNames.Login,
                    LoginInput?.UserNameOrEmailAddress);
            }
            catch (AbpAdminOperationRateLimitingException ex)
            {
                Alerts.Danger(_abpAdminLocalizer[ex.Code ?? AbpAdminDomainErrorCodes.RateLimiting.OperationRateLimitExceeded]);
                return Page();
            }

            try
            {
                await _captchaManager.ValidateAsync(TurnstileToken, CaptchaId, CaptchaCode);
            }
            catch (BusinessException ex)
            {
                Alerts.Danger(_abpAdminLocalizer[ex.Code ?? AbpAdminDomainErrorCodes.Account.CaptchaFailed]);
                return Page();
            }
        }

        // prompt=login 一次性生效（OpenIddict 官方建议的回跳摘除）：SPA 在租户上下文发起授权
        // 时带 prompt=login（防 IdP 残留会话把宿主/其它租户身份静默带进本租户）。基类登录成功后
        // 原样回跳 authorize、prompt 仍在 → OpenIddict 再强制出登录页 → 无限回环（全量实测：
        // fin-ops 已 LoginSucceeded 仍被打回登录表单）。prompt 只在首次出表单时生效，认证完成后
        // 从回跳 URL 摘除，其余 query（state 等）原样保留不做解码重编码。
        // 成功回跳共三条路径，全部必须过摘除：密码（此处）、Passkey（OnPostPasskeyAsync）、
        // 2FA（TwoFactorLoginResultAsync 转跳时摘，完成后原样回跳已是干净 URL）——漏一条，
        // 对应登录方式的用户就在租户上下文复现回环。
        return StripOneTimePromptFromRedirect(await base.OnPostAsync(action));
    }

    private static IActionResult StripOneTimePromptFromRedirect(IActionResult result)
    {
        if (result is not RedirectResult { Url: not null } redirect
            || !redirect.Url.Contains("/connect/authorize", StringComparison.Ordinal))
        {
            return result;
        }

        var stripped = StripPromptFromUrl(redirect.Url);
        return stripped == redirect.Url
            ? result
            : new RedirectResult(stripped, redirect.Permanent, redirect.PreserveMethod);
    }

    /// <summary>
    /// 从 URL 的 query 里摘除 prompt 参数（大小写不敏感、全部同名参数）。
    /// 其余参数（state 等）在原始串上做纯字符串手术、不解码重编码——避免 base64url 值经
    /// ParseQuery 的 +→空格语义漂移。非 authorize 的 URL 由调用方先判，本函数不区分。
    /// </summary>
    internal static string StripPromptFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url ?? string.Empty;
        }

        var parts = url.Split('?', 2);
        if (parts.Length < 2)
        {
            return url;
        }

        var strippedQuery = System.Text.RegularExpressions.Regex.Replace(
            parts[1],
            "(^|&)" + System.Text.RegularExpressions.Regex.Escape("prompt") + "=[^&]*",
            "$1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (strippedQuery.StartsWith('&'))
        {
            strippedQuery = strippedQuery[1..];
        }

        // 中段摘除会给两侧各留一个分隔符（a=1&&b=2）；& 在原始 query 串里只作分隔符
        // （值内 & 必为 %26），归并安全。首/尾悬空 & 一并清理。
        strippedQuery = strippedQuery.Replace("&&", "&");
        if (strippedQuery.EndsWith('&'))
        {
            strippedQuery = strippedQuery[..^1];
        }

        return strippedQuery == parts[1]
            ? url
            : parts[0] + (strippedQuery.Length > 0 ? "?" + strippedQuery : string.Empty);
    }

    public virtual async Task<IActionResult> OnPostPasskeyAsync()
    {
        await LoadCaptchaAsync();
        try
        {
            // Passkey 与密码登录同一入口；跳过验证码等于给断言接口一条无挑战路径。
            await _captchaManager.ValidateAsync(TurnstileToken, CaptchaId, CaptchaCode);
        }
        catch (BusinessException ex)
        {
            Alerts.Danger(_abpAdminLocalizer[ex.Code ?? AbpAdminDomainErrorCodes.Account.CaptchaFailed]);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(PasskeyCredentialJson))
        {
            Alerts.Danger(_abpAdminLocalizer[AbpAdminDomainErrorCodes.Account.PasskeyAssertionFailed]);
            return Page();
        }

        var result = await SignInManager.PasskeySignInAsync(PasskeyCredentialJson);
        if (!result.Succeeded)
        {
            Alerts.Danger(_abpAdminLocalizer[AbpAdminDomainErrorCodes.Account.PasskeyAssertionFailed]);
            return Page();
        }

        // Passkey 成功回跳与密码登录同一治理（见 OnPostAsync 处注释）：漏摘则 Passkey 用户
        // 在租户上下文复现 authorize 回环。
        return StripOneTimePromptFromRedirect(await RedirectSafelyAsync(ReturnUrl ?? "~/", ReturnUrlHash));
    }

    protected override Task<IActionResult> TwoFactorLoginResultAsync()
    {
        // 密码因素已在此处成功：转 2FA 前就摘掉 ReturnUrl 的 prompt——2FA 完成后原样回跳
        // authorize 已是干净 URL；若留到完成后再摘，还得覆写 TwoFactorVerification 页面。
        var returnUrl = StripPromptFromUrl(ReturnUrl);
        return Task.FromResult<IActionResult>(RedirectToPage("./TwoFactorVerification", new
        {
            returnUrl,
            returnUrlHash = ReturnUrlHash,
            rememberMe = LoginInput.RememberMe
        }));
    }

    protected override async Task<List<LoginModel.ExternalProviderModel>> GetExternalProviders()
    {
        var providers = await base.GetExternalProviders();
        var visible = new List<LoginModel.ExternalProviderModel>();

        foreach (var provider in providers)
        {
            var enabled = await SettingProvider.GetOrNullAsync(
                $"AbpAdmin.Account.ExternalLogin.{provider.AuthenticationScheme}.Enabled");
            if (enabled?.Equals("true", StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            var clientId = await SettingProvider.GetOrNullAsync(
                $"AbpAdmin.Account.ExternalLogin.{provider.AuthenticationScheme}.ClientId");
            if (clientId.IsNullOrWhiteSpace())
            {
                continue;
            }

            visible.Add(provider);
        }

        return visible;
    }

    private async Task LoadCaptchaAsync()
    {
        await this.LoadCaptchaAsync(_captchaManager, SettingProvider);
        CaptchaEnabled = CaptchaProvider != null;
    }
}
