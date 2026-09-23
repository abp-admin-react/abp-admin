using System.Threading.Tasks;
using AbpAdmin.Settings;
using EasyAbp.Abp.WeChat.Pay.Options;
using EasyAbp.Abp.WeChat.Pay.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace AbpAdmin.Payments;

/// <summary>
/// T4.7：微信支付凭据优先读加密 SettingUi 项 <c>AbpAdmin.Payment.WeChatPay.*</c>，
/// 空值再回落到 EasyAbp 自带设置。商户号未配时不抛异常，避免 Host 启动失败。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAbpWeChatPayOptionsProvider))]
public class AbpAdminWeChatPayOptionsProvider : IAbpWeChatPayOptionsProvider, ITransientDependency
{
    private readonly ISettingProvider _settingProvider;

    public AbpAdminWeChatPayOptionsProvider(ISettingProvider settingProvider)
    {
        _settingProvider = settingProvider;
    }

    public virtual async Task<AbpWeChatPayOptions> GetAsync(string? mchId)
    {
        var mch = FirstNonEmpty(
            await _settingProvider.GetOrNullAsync(AbpAdminSettings.Payment.WeChatPayMchId),
            await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.MchId),
            mchId);

        var certificate = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Payment.WeChatPayCertificate);
        var certificateBlobName = IsBlobName(certificate)
            ? certificate
            : await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.CertificateBlobName);

        return new AbpWeChatPayOptions
        {
            MchId = mch ?? string.Empty,
            ApiV3Key = FirstNonEmpty(
                await _settingProvider.GetOrNullAsync(AbpAdminSettings.Payment.WeChatPayApiKey),
                await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.ApiKey)),
            IsSandBox = await _settingProvider.GetAsync<bool>(AbpWeChatPaySettings.IsSandBox),
            NotifyUrl = FirstNonEmpty(
                await _settingProvider.GetOrNullAsync(AbpAdminSettings.Payment.WeChatPayNotifyUrl),
                await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.NotifyUrl)),
            RefundNotifyUrl = await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.RefundNotifyUrl),
            CertificateBlobContainerName =
                await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.CertificateBlobContainerName),
            CertificateBlobName = certificateBlobName,
            CertificateSecret = FirstNonEmpty(
                await _settingProvider.GetOrNullAsync(AbpAdminSettings.Payment.WeChatPayCertificateSecret),
                await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.CertificateSecret)),
            PublicKeyId = await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.PublicKeyId),
            PublicKey = await _settingProvider.GetOrNullAsync(AbpWeChatPaySettings.PublicKey)
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsBlobName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Contains('\n') && !value.Contains("BEGIN");
    }
}
