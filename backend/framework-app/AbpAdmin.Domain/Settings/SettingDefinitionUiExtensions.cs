using EasyAbp.Abp.SettingUi;
using Volo.Abp.Settings;

namespace AbpAdmin.Settings;

public static class SettingDefinitionUiExtensions
{
    /// <summary>
    /// 一次性挂上 SettingUi 的 Group1/Group2/Type（及可选 Options）元数据，
    /// 替代每个设置项 3-4 连的 WithProperty 样板。
    /// </summary>
    /// <param name="definition">设置定义。</param>
    /// <param name="group1">一级 Tab（如 Sms / Payment，见 <see cref="AbpAdminSettingUiConsts.Groups"/>）。</param>
    /// <param name="group2">二级分块；null 表示不挂 Group2 属性（如 Saas 域整组无二级分块）。</param>
    /// <param name="type">控件类型（SettingUiConst.Components.* 或 AbpAdminSettingUiConsts.PasswordType）。</param>
    /// <param name="options">select 控件的选项串（EasyAbp 约定 "A|B|C"，空串开头表示含空选项）。</param>
    public static SettingDefinition WithUi(
        this SettingDefinition definition,
        string group1,
        string? group2,
        string type,
        string? options = null)
    {
        definition
            .WithProperty(SettingUiConst.Group1, group1);

        if (group2 != null)
        {
            definition.WithProperty(SettingUiConst.Group2, group2);
        }

        definition.WithProperty(SettingUiConst.Type, type);

        if (options != null)
        {
            definition.WithProperty(SettingUiConst.Options, options);
        }

        return definition;
    }
}
