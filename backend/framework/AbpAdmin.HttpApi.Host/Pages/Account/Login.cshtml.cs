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

        return await base.OnPostAsync(action);
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

        return await RedirectSafelyAsync(ReturnUrl ?? "~/", ReturnUrlHash);
    }

    protected override Task<IActionResult> TwoFactorLoginResultAsync()
    {
        return Task.FromResult<IActionResult>(RedirectToPage("./TwoFactorVerification", new
        {
            returnUrl = ReturnUrl,
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
