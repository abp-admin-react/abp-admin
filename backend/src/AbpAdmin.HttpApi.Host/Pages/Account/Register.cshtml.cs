using System;
using System.Threading.Tasks;
using AbpAdmin.Captcha;
using AbpAdmin.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Identity;
using Volo.Abp.Settings;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// 替换开源包注册页：提交前强制验证码（匿名建账号入口，与登录页同一套 Turnstile/图形双模式），
/// 注册本体与邮箱确认流程全部走基类，不复制业务逻辑。
/// </summary>
public class AbpAdminRegisterModel : RegisterModel, IAccountCaptchaPageModel
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

    public AbpAdminRegisterModel(
        IAccountAppService accountAppService,
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        LoginCaptchaManager captchaManager,
        IStringLocalizer<AbpAdminResource> abpAdminLocalizer,
        ISettingProvider settingProvider)
        : base(accountAppService, schemeProvider, accountOptions,
            identityDynamicClaimsPrincipalContributorCache)
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

        // 验证码先于任何注册副作用；失败时旧图已被一次性消费，重渲染后分部脚本自动取新图
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
