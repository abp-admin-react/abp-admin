using Volo.Abp.Identity.Settings;
using Volo.Abp.Settings;

namespace AbpAdmin.Identity;

/// <summary>
/// 开发便利：放宽框架 4 项密码复杂度默认值（特殊字符/小写/大写/数字），仅保留长度约束。
///
/// 【注意】此弱化对**所有环境**生效（ABP 的 SettingDefinitionProvider 无法注入 IHostEnvironment
/// 做环境区分）。生产部署如需强密码策略，请通过设置管理（SettingManagement / appsettings 种子）
/// 把 Abp.Identity.Password.Require* 四项显式改回 true——设置存储值优先于这里的默认值。
/// （见重构报告问题 16：原实现无任何说明文档，生产默认即弱策略。）
/// </summary>
public class ChangeIdentityPasswordPolicySettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        SetDefaultToFalse(context, IdentitySettingNames.Password.RequireNonAlphanumeric);
        SetDefaultToFalse(context, IdentitySettingNames.Password.RequireLowercase);
        SetDefaultToFalse(context, IdentitySettingNames.Password.RequireUppercase);
        SetDefaultToFalse(context, IdentitySettingNames.Password.RequireDigit);
    }

    private static void SetDefaultToFalse(ISettingDefinitionContext context, string settingName)
    {
        var setting = context.GetOrNull(settingName);
        if (setting != null)
        {
            setting.DefaultValue = false.ToString();
        }
    }
}
