using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

/// <summary>
/// System 域设置（Group1 = System）。原 AbpAdminSettingDefinitionProvider 按域拆分之一，
/// 设置名与行为完全不变；Group/Type 元数据经 WithUi 扩展挂载。
/// </summary>
public class SystemSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                AbpAdminSettings.System.SiteTitle,
                "AbpAdmin",
                L("DisplayName:AbpAdmin.SiteTitle"),
                L("Description:AbpAdmin.SiteTitle"))
                .WithUi(AbpAdminSettingUiConsts.Groups.System, "General", SettingUiConst.Components.Text),

            new SettingDefinition(
                AbpAdminSettings.System.MaintenanceMode,
                "false",
                L("DisplayName:AbpAdmin.MaintenanceMode"),
                L("Description:AbpAdmin.MaintenanceMode"))
                .WithUi(AbpAdminSettingUiConsts.Groups.System, "General", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.System.MaxPageSize,
                "50",
                L("DisplayName:AbpAdmin.MaxPageSize"),
                L("Description:AbpAdmin.MaxPageSize"))
                .WithUi(AbpAdminSettingUiConsts.Groups.System, "General", SettingUiConst.Components.Number),

            new SettingDefinition(
                AbpAdminSettings.System.DefaultTheme,
                "Light",
                L("DisplayName:AbpAdmin.DefaultTheme"),
                L("Description:AbpAdmin.DefaultTheme"))
                .WithUi(AbpAdminSettingUiConsts.Groups.System, "Appearance", SettingUiConst.Components.Select,
                    options: "|Light|Dark|Auto"),

            // T1.4.8 验收：新增 SettingDefinition 不改前端即出现
            new SettingDefinition(
                AbpAdminSettings.System.WelcomeMessage,
                "Welcome to AbpAdmin!",
                L("DisplayName:AbpAdmin.WelcomeMessage"),
                L("Description:AbpAdmin.WelcomeMessage"))
                .WithUi(AbpAdminSettingUiConsts.Groups.System, "General", SettingUiConst.Components.Text)
        );

        // T3.5：SMTP 等 9 个邮件设置的 SettingUi 元数据由 EasyAbp.Abp.SettingUi 自带 JSON 提供
        //（Group1=Mail，2.10.0 反编译核实；该 JSON 与应用侧重复定义同名 key 会让合并直接抛
        // duplicate key，所以不要在我们的 abpadmin.json 里再写一遍）。
        // 加密项的密码框与脱敏统一在 Application 层 AbpAdminSettingUiAppService 里做
        //（定义侧改动取决于 provider 执行顺序，不可控，已实测 GetOrNull 拿不到）。
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
