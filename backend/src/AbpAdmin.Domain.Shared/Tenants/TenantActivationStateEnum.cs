using System.ComponentModel;
using AbpAdmin.DataDictionaries;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户激活状态三态（T2.8）。
/// T3.4：以 Enum 结尾的公开枚举会被种子同步为静态数据字典（字典编码 TenantActivationState），
/// 显示名来自成员上的 [Description]，标签颜色来自 [DictionaryTag]，改枚举成员后重新跑 DbMigrator 即可。
/// </summary>
[Description("租户激活状态")]
public enum TenantActivationStateEnum : byte
{
    /// <summary>
    /// 激活（无期限）
    /// </summary>
    [Description("激活")]
    [DictionaryTag(DataDictionaryTagTypes.Green)]
    Active = 0,

    /// <summary>
    /// 限时激活（到 ActivationEndDate 后失效）
    /// </summary>
    [Description("限时激活")]
    [DictionaryTag(DataDictionaryTagTypes.Orange)]
    ActiveWithLimitedTime = 1,

    /// <summary>
    /// 停用
    /// </summary>
    [Description("已停用")]
    [DictionaryTag(DataDictionaryTagTypes.Red)]
    Passive = 2
}
