using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

/// <summary>
/// Sms 域设置（Group1 = Sms）。原 AbpAdminSettingDefinitionProvider 按域拆分之一，设置名与行为完全不变。
/// </summary>
public class SmsSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // ========== T3.5 通知服务：短信渠道 ==========
        // 厂商凭据必须加密（isEncrypted: true）；明文值永不下发前端（见 Application 层
        // AbpAdminSettingUiAppService 的脱敏覆写），提交空值表示不修改。
        context.Add(
            new SettingDefinition(
                AbpAdminSettings.Sms.Provider,
                AbpAdmin.Sms.AbpAdminSmsProviders.Null,
                L("DisplayName:AbpAdmin.Sms.Provider"),
                L("Description:AbpAdmin.Sms.Provider"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "Provider", SettingUiConst.Components.Select,
                    options: "Null|Aliyun|TencentCloud"),

            new SettingDefinition(
                AbpAdminSettings.Sms.VerificationCodeTemplateCode,
                null,
                L("DisplayName:AbpAdmin.Sms.VerificationCode.TemplateCode"),
                L("Description:AbpAdmin.Sms.VerificationCode.TemplateCode"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "Provider", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.AliyunAccessKeyId,
                null,
                L("DisplayName:AbpAdmin.Sms.Aliyun.AccessKeyId"),
                L("Description:AbpAdmin.Sms.Aliyun.AccessKeyId"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "Aliyun", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.AliyunAccessKeySecret,
                null,
                L("DisplayName:AbpAdmin.Sms.Aliyun.AccessKeySecret"),
                L("Description:AbpAdmin.Sms.Aliyun.AccessKeySecret"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "Aliyun", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Sms.AliyunSignName,
                null,
                L("DisplayName:AbpAdmin.Sms.Aliyun.SignName"),
                L("Description:AbpAdmin.Sms.Aliyun.SignName"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "Aliyun", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.TencentCloudSecretId,
                null,
                L("DisplayName:AbpAdmin.Sms.TencentCloud.SecretId"),
                L("Description:AbpAdmin.Sms.TencentCloud.SecretId"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "TencentCloud", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.TencentCloudSecretKey,
                null,
                L("DisplayName:AbpAdmin.Sms.TencentCloud.SecretKey"),
                L("Description:AbpAdmin.Sms.TencentCloud.SecretKey"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "TencentCloud", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Sms.TencentCloudSmsSdkAppid,
                null,
                L("DisplayName:AbpAdmin.Sms.TencentCloud.SmsSdkAppid"),
                L("Description:AbpAdmin.Sms.TencentCloud.SmsSdkAppid"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "TencentCloud", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.TencentCloudSign,
                null,
                L("DisplayName:AbpAdmin.Sms.TencentCloud.Sign"),
                L("Description:AbpAdmin.Sms.TencentCloud.Sign"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "TencentCloud", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.TencentCloudEndPoint,
                "sms.tencentcloudapi.com",
                L("DisplayName:AbpAdmin.Sms.TencentCloud.EndPoint"),
                L("Description:AbpAdmin.Sms.TencentCloud.EndPoint"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "TencentCloud", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Sms.TencentCloudRegion,
                "ap-guangzhou",
                L("DisplayName:AbpAdmin.Sms.TencentCloud.Region"),
                L("Description:AbpAdmin.Sms.TencentCloud.Region"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "TencentCloud", SettingUiConst.Components.Text)
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
