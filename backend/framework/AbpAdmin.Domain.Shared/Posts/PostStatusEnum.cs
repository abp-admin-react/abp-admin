using System.ComponentModel;

namespace AbpAdmin.Posts;

/// <summary>
/// 岗位状态。以 Enum 结尾的公开枚举会被种子同步为静态数据字典（字典编码 PostStatus）。
/// </summary>
[Description("岗位状态")]
public enum PostStatusEnum
{
    /// <summary>启用</summary>
    [Description("启用")]
    Enabled = 0,

    /// <summary>停用</summary>
    [Description("停用")]
    Disabled = 1
}
