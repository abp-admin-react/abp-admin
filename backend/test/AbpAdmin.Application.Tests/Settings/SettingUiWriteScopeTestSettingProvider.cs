using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Settings;

namespace AbpAdmin;

/// <summary>
/// SettingUi 写入面作用域测试的专用设置定义（安全审计 run-2 C-3）：
/// DomainOnlyName 复刻资金开关形态（G+D providers、故意 omit WithUi——域权限端点是唯一写门），
/// UiRegisteredName 是对照的正常 UI 注册设置。经 AbpSettingOptions.DefinitionProviders
/// 由 EfCore 侧启动模块注册。
/// </summary>
public class SettingUiWriteScopeTestSettingProvider : SettingDefinitionProvider
{
    public const string DomainOnlyName = "AbpAdminTest.DomainOnly";
    public const string UiRegisteredName = "AbpAdminTest.UiRegistered";

    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(DomainOnlyName, "domain-default", isVisibleToClients: false)
                .WithProviders(GlobalSettingValueProvider.ProviderName, DefaultValueSettingValueProvider.ProviderName),

            new SettingDefinition(UiRegisteredName, "ui-default", isVisibleToClients: false)
                .WithUi(AbpAdminSettingUiConsts.Groups.Sms, "WriteScopeTest", SettingUiConst.Components.Text));
    }
}
