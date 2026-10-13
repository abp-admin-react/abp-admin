using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// T2.7 双因素第二步；T4.2 增加 Authenticator 作为 Provider。
/// </summary>
public class TwoFactorVerificationModel : AccountPageModel
{
    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrlHash { get; set; }

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    /// <summary>
    /// 记住此浏览器（Identity 的 RememberBrowser cookie，RememberClient 机制）：
    /// 勾选后该浏览器在 cookie 有效期内再次登录免二次验证。仅对验证码路径生效；
    /// 恢复码是紧急路径，不提供记忆（TwoFactorRecoveryCodeSignInAsync 无此参数）。
    /// </summary>
    [BindProperty]
    public bool RememberBrowser { get; set; }

    [BindProperty]
    [Required]
    public string? Code { get; set; }

    /// <summary>
    /// 恢复码（启用 Authenticator 时一次性生成）。不加 [Required]：与 Code 共用
    /// BindProperty，验证码表单提交时它为 null 属正常状态，校验收在恢复码 handler 内。
    /// </summary>
    [BindProperty]
    public string? RecoveryCode { get; set; }

    [BindProperty]
    public string Provider { get; set; } = "Email";

    public List<string> Providers { get; private set; } = new();

    public bool CodeSent { get; set; }

    public bool IsAuthenticator => Provider == TokenOptions.DefaultAuthenticatorProvider;

    private readonly IOperationRateLimitingChecker _rateLimitingChecker;
    private readonly IEmailSender _emailSender;
    private readonly IStringLocalizer<AbpAdminResource> _abpAdminLocalizer;
    private readonly Volo.Abp.Sms.ISmsSender _smsSender;
    private readonly AbpAdmin.Sms.VerificationCodeSmsMessageFactory _smsMessageFactory;

    public TwoFactorVerificationModel(
        IOperationRateLimitingChecker rateLimitingChecker,
        IEmailSender emailSender,
        IStringLocalizer<AbpAdminResource> abpAdminLocalizer,
        Volo.Abp.Sms.ISmsSender smsSender,
        AbpAdmin.Sms.VerificationCodeSmsMessageFactory smsMessageFactory)
    {
        _rateLimitingChecker = rateLimitingChecker;
        _emailSender = emailSender;
        _abpAdminLocalizer = abpAdminLocalizer;
        _smsSender = smsSender;
        _smsMessageFactory = smsMessageFactory;
    }

    public virtual async Task<IActionResult> OnGetAsync()
    {
        var user = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            return RedirectToLogin();
        }

        await LoadProvidersAsync(user);
        return Page();
    }

    public virtual async Task<IActionResult> OnPostSendCodeAsync()
    {
        var user = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            return RedirectToLogin();
        }

        await LoadProvidersAsync(user);
        if (IsAuthenticator)
        {
            return Page();
        }

        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.TwoFactorCode, user.Id.ToString());

        var code = await UserManager.GenerateTwoFactorTokenAsync(user, Provider);
        if (Provider == "Email")
        {
            await _emailSender.SendAsync(
                user.Email!,
                _abpAdminLocalizer["TwoFactorCodeSubject"],
                _abpAdminLocalizer["TwoFactorCodeBody", code]);
        }
        else
        {
            await _smsSender.SendAsync(await _smsMessageFactory.BuildAsync(user.PhoneNumber!, code));
        }

        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());

        CodeSent = true;
        Alerts.Info(_abpAdminLocalizer["TwoFactorCodeSent"], null, true);
        return Page();
    }

    public virtual async Task<IActionResult> OnPostVerifyAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            return RedirectToLogin();
        }

        await LoadProvidersAsync(user);
        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());

        var result = await SignInManager.TwoFactorSignInAsync(Provider, Code!, RememberMe, rememberClient: RememberBrowser);
        if (!result.Succeeded)
        {
            Alerts.Warning(_abpAdminLocalizer[AbpAdminDomainErrorCodes.Account.InvalidTwoFactorCode], null, true);
            return Page();
        }

        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());
        return await RedirectSafelyAsync(ReturnUrl ?? "~/", ReturnUrlHash);
    }

    /// <summary>
    /// 恢复码登录（验证器不可用时的兜底路径）。TwoFactorRecoveryCodeSignInAsync 内部
    /// Redeem 恢复码（每个一次性）并完成 2FA 登录；失败计入锁定（与验证码失败同口径），防穷举。
    /// .NET 10 Identity 已移除 TokenOptions.DefaultRecoveryProvider 常量，统一走该专用 API。
    /// </summary>
    public virtual async Task<IActionResult> OnPostVerifyWithRecoveryCodeAsync()
    {
        // 不检查 ModelState.IsValid：Code 的 [Required] 与本 handler 无关
        if (string.IsNullOrWhiteSpace(RecoveryCode))
        {
            Alerts.Warning(_abpAdminLocalizer[AbpAdminDomainErrorCodes.Account.InvalidRecoveryCode], null, true);
            return Page();
        }

        var user = await SignInManager.GetTwoFactorAuthenticationUserAsync();
        if (user == null)
        {
            return RedirectToLogin();
        }

        await LoadProvidersAsync(user);
        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());

        var result = await SignInManager.TwoFactorRecoveryCodeSignInAsync(RecoveryCode!);
        if (!result.Succeeded)
        {
            Alerts.Warning(_abpAdminLocalizer[AbpAdminDomainErrorCodes.Account.InvalidRecoveryCode], null, true);
            return Page();
        }

        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());
        return await RedirectSafelyAsync(ReturnUrl ?? "~/", ReturnUrlHash);
    }

    private async Task LoadProvidersAsync(IdentityUser user)
    {
        var valid = (await UserManager.GetValidTwoFactorProvidersAsync(user)).ToList();
        Providers = valid;
        if (!valid.Contains(Provider))
        {
            Provider = valid.Contains(TokenOptions.DefaultAuthenticatorProvider)
                ? TokenOptions.DefaultAuthenticatorProvider
                : valid.Contains("Email")
                    ? "Email"
                    : valid.FirstOrDefault() ?? "Email";
        }
    }

    private IActionResult RedirectToLogin()
    {
        return RedirectToPage("./Login", new { ReturnUrl, ReturnUrlHash });
    }
}
