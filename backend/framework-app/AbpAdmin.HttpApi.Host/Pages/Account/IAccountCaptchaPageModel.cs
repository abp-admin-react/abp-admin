using System.Threading.Tasks;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// 账号页验证码渲染约定：实现本接口的页面模型可直接把自身传给
/// _AccountCaptcha.cshtml 分部页（渲染 Turnstile / 图形验证码及其脚本）。
/// 属性值经 <see cref="AccountCaptchaPageModelExtensions.LoadCaptchaAsync"/> 填充
/// （审查轮收拢三个页面模型各自复制的 Load 体）。
/// </summary>
public interface IAccountCaptchaPageModel
{
    /// <summary>当前生效的验证码提供者：Turnstile | Image | null（未启用）。</summary>
    string? CaptchaProvider { get; set; }

    string? CaptchaSiteKey { get; set; }
}

/// <summary>
/// Load 管线的单一实现：Login 页另有 CaptchaEnabled 派生标记，在调用后自行补齐。
/// 注意必须在每个 OnGet/OnPost 入口调用——属性不参与 model binding，POST 重渲染
/// 时若未加载，分部页会因 Provider 为 null 整段不渲染（用户将无验证码可解）。
/// </summary>
public static class AccountCaptchaPageModelExtensions
{
    public static async Task LoadCaptchaAsync(
        this IAccountCaptchaPageModel model,
        AbpAdmin.Captcha.LoginCaptchaManager captchaManager,
        Volo.Abp.Settings.ISettingProvider settingProvider)
    {
        model.CaptchaProvider = await captchaManager.GetProviderAsync();
        model.CaptchaSiteKey = await settingProvider.GetOrNullAsync(
            AbpAdmin.Settings.AbpAdminSettings.Account.CaptchaSiteKey);
    }
}
