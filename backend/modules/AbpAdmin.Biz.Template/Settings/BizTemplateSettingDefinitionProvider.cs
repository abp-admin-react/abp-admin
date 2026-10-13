using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Biz.Template.Settings;

/// <summary>
/// 模块运行期业务参数定义（配置三层约定的第③层，参数名见 <see cref="BizTemplateSettings"/>）。
///
/// 免注册：ABP 对模块程序集里的 SettingDefinitionProvider 自动发现（框架 System/Audit/Sms 等
/// 域同机制，框架代码里没有任何显式注册）——宿主与 DbMigrator DependsOn 本模块后即生效，
/// 模块接线只有 csproj 引用 + DependsOn 一行，不因配置多出接线步骤。
///
/// 存储复用框架 SettingManagement：设置页改值 → SettingManagement 表（按主机/租户），
/// SettingProvider 取值时存储值优先、无存储值回落到这里的 DefaultValue。模块不建参数表、不写缓存。
///
/// UI 元数据（WithUi）：Group1 用模块自带 Tab 名（<see cref="BizTemplateSettings.SettingUiGroup"/>），
/// 前端设置页按 Group1 动态出 Tab，模块获得独立设置分组且不改前端；
/// 控件类型还有 Checkbox/Select/Text/Password（框架 AbpAdmin.Settings 域各有实例，照抄即可）。
///
/// 【必须 WithProviders("G")】不声明 Providers 时，EasyAbp SettingUi 的 host 侧保存会路由到
/// T 层且 ProviderKey 为空，而 host 读链对该层的命中在缓存/DB/重启间表现不一致（值被搁浅，
/// 保存"成功"却不生效）——实测复现过。显式声明 G 后：host 写/读/重置全程钉在 G 层自洽；
/// 租户侧由框架 AbpAdminSettingUiAppService 的钳制写 T(own)、重置清 U+T(own)，G 层不受租户重置影响。
/// </summary>
public class BizTemplateSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                BizTemplateSettings.Project.MaxProjectsPerUser,
                // 默认值单源：取 BizTemplateSettings.Project.DefaultMaxProjectsPerUser（int 转
                // 十进制字符串文化稳定），fail-closed 兜底读的是同一个常量，不存在两处 100 漂移
                BizTemplateSettings.Project.DefaultMaxProjectsPerUser.ToString(),
                L("DisplayName:BizTemplate.Project.MaxProjectsPerUser"),
                L("Description:BizTemplate.Project.MaxProjectsPerUser"))
                // 声明 Providers 会裁剪 SettingProvider 的读链——必须同时声明 "D"(默认值),
                // 否则无存储值时连定义默认值都读不到(返回 null)。G 管写路由(host 保存落 G 层),
                // D 保读链兜底,两者缺一不可。
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(BizTemplateSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Number));
    }

    // 本地化资源用模块自带的 BizTemplateResource——设置项显示名/描述随模块自持，
    // 不写进框架集中资源文件（模块可整体搬走的纪律之一）。
    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<BizTemplateResource>(name);
    }
}
