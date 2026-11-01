using System;
using System.Threading.Tasks;
using AbpAdmin.Captcha;
using AbpAdmin.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Settings;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// 替换开源包忘记密码页：发码（匿名触发邮件）前强制验证码，防刷信/防邮箱轰炸；
/// 后续发码与防枚举语义走基类（IAccountAppService.SendPasswordResetCodeAsync）。
/// </summary>
public class AbpAdminForgotPasswordModel : ForgotPasswordModel, IAccountCaptchaPageModel
{
    [BindProperty(Name = TurnstileCaptchaValidator.FormFieldName)]
    public string? TurnstileToken { get; set; }

    /// <summary>图形验证码（Image provider）：图片标识与用户输入。</summary>
    [BindProperty]
    public Guid? CaptchaId { get; set; }

    [BindProperty]
    public string? CaptchaCode { get; set; }

    /// <summary>当前生效的验证码提供者：Turnstile | Image | null（未启用）。</summary>
    public string? CaptchaProvider { get; set; }

    public string? CaptchaSiteKey { get; set; }

    private readonly LoginCaptchaManager _captchaManager;
    private readonly IStringLocalizer<AbpAdminResource> _abpAdminLocalizer;
    private readonly ISettingProvider _settingProvider;

    public AbpAdminForgotPasswordModel(
        LoginCaptchaManager captchaManager,
        IStringLocalizer<AbpAdminResource> abpAdminLocalizer,
        ISettingProvider settingProvider)
    {
        _captchaManager = captchaManager;
        _abpAdminLocalizer = abpAdminLocalizer;
        _settingProvider = settingProvider;
    }

    public override async Task<IActionResult> OnGetAsync()
    {
        await this.LoadCaptchaAsync(_captchaManager, _settingProvider);
        return await base.OnGetAsync();
    }

    public override async Task<IActionResult> OnPostAsync()
    {
        // 每个入口（含 POST）都必须先加载验证码状态：属性不参与 model binding，
        // 漏加载会让分部页在重渲染时整段不渲染，用户无验证码可解（审查 H1）
        await this.LoadCaptchaAsync(_captchaManager, _settingProvider);

        // 验证码先于发码副作用；失败时旧图已被一次性消费，重渲染后分部脚本自动取新图
        try
        {
            await _captchaManager.ValidateAsync(TurnstileToken, CaptchaId, CaptchaCode);
        }
        catch (BusinessException ex)
        {
            Alerts.Danger(_abpAdminLocalizer[ex.Code ?? AbpAdminDomainErrorCodes.Account.CaptchaFailed]);
            return Page();
        }

        return await base.OnPostAsync();
    }
}
