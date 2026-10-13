using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace AbpAdmin.Captcha;

/// <summary>
/// T4.4：校验 Cloudflare Turnstile。开关关闭时直接通过。
/// HTTP 调用走 ITurnstileSiteVerifyApi 声明式客户端（HttpAgent，绑定 "Turnstile" 命名 HttpClient）。
/// </summary>
public class TurnstileCaptchaValidator : DomainService
{
    public const string FormFieldName = "cf-turnstile-response";

    private readonly ISettingProvider _settingProvider;
    private readonly ITurnstileSiteVerifyApi _siteVerifyApi;

    public TurnstileCaptchaValidator(
        ISettingProvider settingProvider,
        ITurnstileSiteVerifyApi siteVerifyApi)
    {
        _settingProvider = settingProvider;
        _siteVerifyApi = siteVerifyApi;
    }

    public virtual async Task<bool> IsEnabledAsync()
    {
        return await _settingProvider.IsTrueAsync(AbpAdminSettings.Account.CaptchaEnabled);
    }

    public virtual async Task ValidateAsync(string? token)
    {
        if (!await IsEnabledAsync())
        {
            return;
        }

        var secret = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Account.CaptchaSecretKey);
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.CaptchaNotConfigured);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.CaptchaFailed);
        }

        using var result = await _siteVerifyApi.VerifyAsync(new Dictionary<string, string>
        {
            ["secret"] = secret!,
            ["response"] = token!
        });

        // 非 2xx 视为校验失败（Cloudflare 5xx 时响应体非标准 JSON，不解析错误码）
        var payload = result.IsSuccessStatusCode ? result.Result : null;
        if (payload?.Success != true)
        {
            Logger.LogWarning("Turnstile 校验失败: HTTP {StatusCode} {ErrorCodes}",
                (int)result.StatusCode,
                payload?.ErrorCodes == null ? "" : string.Join(",", payload.ErrorCodes));
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.CaptchaFailed);
        }
    }
}
