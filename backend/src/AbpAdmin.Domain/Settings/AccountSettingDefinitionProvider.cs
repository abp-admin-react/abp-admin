using AbpAdmin.Identity;
using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi;
using System;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

/// <summary>
/// Account 域设置（Group1 = Account）：无密码登录/防枚举、会话与登录安全、验证码、外部登录。
/// 原 AbpAdminSettingDefinitionProvider 按域拆分之一，设置名与行为完全不变。
/// </summary>
public class AccountSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            // ========== T2.7 Account Pro 缺口：无密码登录与防枚举设置 ==========
            new SettingDefinition(
                AbpAdminSettings.Account.PasswordlessLoginMode,
                "OtpAndMagicLink",
                L("DisplayName:AbpAdmin.Account.PasswordlessLoginMode"),
                L("Description:AbpAdmin.Account.PasswordlessLoginMode"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Login", SettingUiConst.Components.Select,
                    options: "OtpAndMagicLink|MagicLinkOnly|OtpOnly"),

            new SettingDefinition(
                AbpAdminSettings.Account.PasswordlessLoginTokenLifetimeSeconds,
                "90",
                L("DisplayName:AbpAdmin.Account.PasswordlessLoginTokenLifetimeSeconds"),
                L("Description:AbpAdmin.Account.PasswordlessLoginTokenLifetimeSeconds"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Login", SettingUiConst.Components.Number),

            new SettingDefinition(
                AbpAdminSettings.Account.PreventEmailEnumeration,
                "true",
                L("DisplayName:AbpAdmin.Account.PreventEmailEnumeration"),
                L("Description:AbpAdmin.Account.PreventEmailEnumeration"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Security", SettingUiConst.Components.Checkbox),

            // ========== T4.1 / T4.4 会话与登录安全 ==========
            new SettingDefinition(
                AbpAdminSettings.Account.SessionCleanupInactiveDays,
                "30",
                L("DisplayName:AbpAdmin.Identity.SessionCleanupInactiveDays"),
                L("Description:AbpAdmin.Identity.SessionCleanupInactiveDays"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Session", SettingUiConst.Components.Number),

            // 对标 ABP Identity Pro 的 Prevent Concurrent Login（三档；选项串由枚举名生成，单一出处）
            new SettingDefinition(
                AbpAdminSettings.Account.PreventConcurrentLoginMode,
                "Disabled",
                L("DisplayName:AbpAdmin.Identity.PreventConcurrentLoginMode"),
                L("Description:AbpAdmin.Identity.PreventConcurrentLoginMode"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Session", SettingUiConst.Components.Select,
                    options: string.Join("|", Enum.GetNames<PreventConcurrentLoginMode>())),

            new SettingDefinition(
                AbpAdminSettings.Account.IdleSessionTimeoutMinutes,
                "0",
                L("DisplayName:AbpAdmin.Account.IdleSessionTimeoutMinutes"),
                L("Description:AbpAdmin.Account.IdleSessionTimeoutMinutes"),
                isVisibleToClients: true)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Session", SettingUiConst.Components.Number),

            new SettingDefinition(
                AbpAdminSettings.Account.CaptchaEnabled,
                "false",
                L("DisplayName:AbpAdmin.Account.Captcha.Enabled"),
                L("Description:AbpAdmin.Account.Captcha.Enabled"),
                isVisibleToClients: true)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Captcha", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.Account.CaptchaProvider,
                "Turnstile",
                L("DisplayName:AbpAdmin.Account.Captcha.Provider"),
                L("Description:AbpAdmin.Account.Captcha.Provider"),
                isVisibleToClients: true)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Captcha", SettingUiConst.Components.Select,
                    options: "Turnstile|Image"),

            new SettingDefinition(
                AbpAdminSettings.Account.CaptchaSiteKey,
                null,
                L("DisplayName:AbpAdmin.Account.Captcha.SiteKey"),
                L("Description:AbpAdmin.Account.Captcha.SiteKey"),
                isVisibleToClients: true)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Captcha", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Account.CaptchaSecretKey,
                null,
                L("DisplayName:AbpAdmin.Account.Captcha.SecretKey"),
                L("Description:AbpAdmin.Account.Captcha.SecretKey"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "Captcha", AbpAdminSettingUiConsts.PasswordType),

            // ========== 外部登录（T2.7 GitHub/Microsoft，T4.4 微信/Google） ==========
            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginGitHubEnabled,
                "false",
                L("DisplayName:AbpAdmin.Account.ExternalLogin.GitHub.Enabled"),
                L("Description:AbpAdmin.Account.ExternalLogin.GitHub.Enabled"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginGitHubClientId,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.GitHub.ClientId"),
                L("Description:AbpAdmin.Account.ExternalLogin.GitHub.ClientId"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginGitHubClientSecret,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.GitHub.ClientSecret"),
                L("Description:AbpAdmin.Account.ExternalLogin.GitHub.ClientSecret"),
                isEncrypted: true,
                isVisibleToClients: false)
                // 加密项统一密码框：与 CreateSettingInfoAsync 的强制 Type=password 对齐，
                // 避免"定义侧 text / 服务端改写 password"的口径漂移
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginMicrosoftEnabled,
                "false",
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Microsoft.Enabled"),
                L("Description:AbpAdmin.Account.ExternalLogin.Microsoft.Enabled"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginMicrosoftClientId,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Microsoft.ClientId"),
                L("Description:AbpAdmin.Account.ExternalLogin.Microsoft.ClientId"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginMicrosoftClientSecret,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Microsoft.ClientSecret"),
                L("Description:AbpAdmin.Account.ExternalLogin.Microsoft.ClientSecret"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginWeixinEnabled,
                "false",
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Weixin.Enabled"),
                L("Description:AbpAdmin.Account.ExternalLogin.Weixin.Enabled"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginWeixinClientId,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Weixin.ClientId"),
                L("Description:AbpAdmin.Account.ExternalLogin.Weixin.ClientId"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginWeixinClientSecret,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Weixin.ClientSecret"),
                L("Description:AbpAdmin.Account.ExternalLogin.Weixin.ClientSecret"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", AbpAdminSettingUiConsts.PasswordType),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginGoogleEnabled,
                "false",
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Google.Enabled"),
                L("Description:AbpAdmin.Account.ExternalLogin.Google.Enabled"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginGoogleClientId,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Google.ClientId"),
                L("Description:AbpAdmin.Account.ExternalLogin.Google.ClientId"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.Account.ExternalLoginGoogleClientSecret,
                null,
                L("DisplayName:AbpAdmin.Account.ExternalLogin.Google.ClientSecret"),
                L("Description:AbpAdmin.Account.ExternalLogin.Google.ClientSecret"),
                isEncrypted: true,
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Account, "ExternalLogin", AbpAdminSettingUiConsts.PasswordType)
        );

        // T2.6 Password Aging：密码有效期设置的 SettingUi 元数据在 SettingProperties/abpadmin.json 中配置
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
