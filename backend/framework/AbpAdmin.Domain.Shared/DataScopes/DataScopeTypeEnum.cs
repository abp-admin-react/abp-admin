using System.ComponentModel;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据范围类型。多角色时取并集，任一角色为 <see cref="All"/> 则整体为 <see cref="All"/>。
/// T3.4：以 Enum 结尾的公开枚举会被种子同步为静态数据字典（字典编码 DataScopeType）。
/// </summary>
[Description("数据范围类型")]
public enum DataScopeTypeEnum
{
    /// <summary>全部数据</summary>
    [Description("全部数据")]
    All = 0,

    /// <summary>用户所属全部 OU 各自子树的并集</summary>
    [Description("当前组织单元及子级")]
    CurrentOuAndChildren = 1,

    /// <summary>用户所属全部 OU（不含下级）</summary>
    [Description("仅当前组织单元")]
    CurrentOu = 2,

    /// <summary>指定的组织集合</summary>
    [Description("自定义组织单元")]
    Custom = 3,

    /// <summary>仅本人创建</summary>
    [Description("仅本人数据")]
    SelfOnly = 4
}
