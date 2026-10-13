using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

/// <summary>
/// Saas 域设置（Group1 = Saas，无二级分块）。原 AbpAdminSettingDefinitionProvider 按域拆分之一，
/// 设置名与行为完全不变。
/// </summary>
public class SaasSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // ========== T2.8 SaaS Pro 缺口 ==========
        // 对齐 Pro 的 Volo.Saas.EnableTenantBasedConnectionStringManagement，默认 true。
        // isVisibleToClients: true —— 前端需要通过 application-configuration 读取该值来隐藏连接串管理 UI。
        context.Add(
            new SettingDefinition(
                AbpAdminSettings.Saas.EnableTenantBasedConnectionStringManagement,
                "true",
                L("DisplayName:AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement"),
                L("Description:AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement"),
                isVisibleToClients: true)
                .WithUi(AbpAdminSettingUiConsts.Groups.Saas, group2: null, SettingUiConst.Components.Checkbox)
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
