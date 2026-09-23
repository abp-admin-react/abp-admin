using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

/// <summary>
/// Audit 域设置（Group1 = Audit）。原 AbpAdminSettingDefinitionProvider 按域拆分之一，
/// 设置名与行为完全不变。
/// </summary>
public class AuditSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // T2.1 审计日志过期清理
        context.Add(
            new SettingDefinition(
                AbpAdminSettings.Audit.ExpiredItemDeletionEnabled,
                "true",
                L("DisplayName:AbpAdmin.Audit.ExpiredItemDeletionEnabled"),
                L("Description:AbpAdmin.Audit.ExpiredItemDeletionEnabled"),
                isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Audit, "Cleanup", SettingUiConst.Components.Checkbox),

            new SettingDefinition(
                AbpAdminSettings.Audit.ExpiredItemDeletionPeriodDays,
                "30",
                L("DisplayName:AbpAdmin.Audit.ExpiredItemDeletionPeriodDays"),
                L("Description:AbpAdmin.Audit.ExpiredItemDeletionPeriodDays"))
                .WithUi(AbpAdminSettingUiConsts.Groups.Audit, "Cleanup", SettingUiConst.Components.Number)
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
