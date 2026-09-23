using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

/// <summary>
/// Payment 域设置（Group1 = Payment）。原 AbpAdminSettingDefinitionProvider 按域拆分之一，
/// 设置名与行为完全不变。
/// </summary>
public class PaymentSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // ========== T4.7 微信支付 ==========
        context.Add(
            new SettingDefinition(
                AbpAdminSettings.Payment.WeChatPayMchId,
                null,
                L("DisplayName:AbpAdmin.Payment.WeChatPay.MchId"),
                L("Description:AbpAdmin.Payment.WeChatPay.MchId"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Payment, "WeChatPay", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Payment.WeChatPayApiKey,
                null,
                L("DisplayName:AbpAdmin.Payment.WeChatPay.ApiKey"),
                L("Description:AbpAdmin.Payment.WeChatPay.ApiKey"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Payment, "WeChatPay", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Payment.WeChatPayCertificate,
                null,
                L("DisplayName:AbpAdmin.Payment.WeChatPay.Certificate"),
                L("Description:AbpAdmin.Payment.WeChatPay.Certificate"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Payment, "WeChatPay", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Payment.WeChatPayCertificateSecret,
                null,
                L("DisplayName:AbpAdmin.Payment.WeChatPay.CertificateSecret"),
                L("Description:AbpAdmin.Payment.WeChatPay.CertificateSecret"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Payment, "WeChatPay", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Payment.WeChatPayNotifyUrl,
                null,
                L("DisplayName:AbpAdmin.Payment.WeChatPay.NotifyUrl"),
                L("Description:AbpAdmin.Payment.WeChatPay.NotifyUrl"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Payment, "WeChatPay", SettingUiConst.Components.Text)
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
