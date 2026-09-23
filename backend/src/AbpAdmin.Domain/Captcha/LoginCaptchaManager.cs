using System;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace AbpAdmin.Captcha;

/// <summary>
/// 登录验证码门面：按设置在 Turnstile（外置行为验证）与 Image（自托管图形验证码）之间分发。
/// 登录页（AbpAdminLoginModel）只与本类交互，不感知具体实现。
/// 开关矩阵：Captcha.Enabled=false → 关；Enabled=true 且 Provider=Turnstile → 需 SiteKey
/// （缺 Key 时视为关，与页面渲染条件一致）；Provider=Image → 无外部依赖，开箱可用。
/// </summary>
public class LoginCaptchaManager : DomainService
{
    public const string ProviderTurnstile = "Turnstile";
    public const string ProviderImage = "Image";

    private readonly TurnstileCaptchaValidator _turnstileValidator;
    private readonly ImageCaptchaManager _imageCaptchaManager;
    private readonly ISettingProvider _settingProvider;

    public LoginCaptchaManager(
        TurnstileCaptchaValidator turnstileValidator,
        ImageCaptchaManager imageCaptchaManager,
        ISettingProvider settingProvider)
    {
        _turnstileValidator = turnstileValidator;
        _imageCaptchaManager = imageCaptchaManager;
        _settingProvider = settingProvider;
    }

    /// <summary>当前生效的验证码提供者；未启用返回 null。</summary>
    public virtual async Task<string?> GetProviderAsync()
    {
        if (!await _settingProvider.IsTrueAsync(AbpAdminSettings.Account.CaptchaEnabled))
        {
            return null;
        }

        var provider = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Account.CaptchaProvider);
        if (string.Equals(provider, ProviderImage, StringComparison.OrdinalIgnoreCase))
        {
            return ProviderImage;
        }

        // Turnstile（默认）：没有 SiteKey 时页面渲染不出组件，视为未启用
        var siteKey = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Account.CaptchaSiteKey);
        return string.IsNullOrWhiteSpace(siteKey) ? null : ProviderTurnstile;
    }

    public virtual async Task<bool> IsEnabledAsync()
    {
        return await GetProviderAsync() != null;
    }

    /// <summary>
    /// 校验。Turnstile 走 token；Image 走 (captchaId, captchaCode) 一次性消费。
    /// 失败抛 <see cref="BusinessException"/>（CaptchaFailed / CaptchaNotConfigured）。
    /// </summary>
    public virtual async Task ValidateAsync(string? turnstileToken, Guid? captchaId, string? captchaCode)
    {
        var provider = await GetProviderAsync();
        if (provider == null)
        {
            return; // 未启用 = 直接放行（与原 TurnstileCaptchaValidator 语义一致）
        }

        if (provider == ProviderImage)
        {
            if (captchaId == null || string.IsNullOrWhiteSpace(captchaCode) ||
                !await _imageCaptchaManager.ValidateAndConsumeAsync(captchaId.Value, captchaCode))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Account.CaptchaFailed);
            }

            return;
        }

        await _turnstileValidator.ValidateAsync(turnstileToken);
    }
}
