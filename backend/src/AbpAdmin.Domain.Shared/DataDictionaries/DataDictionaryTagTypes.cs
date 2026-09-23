using System.Linq;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 字典项标签颜色的可选值集合（T3.4 第 12 步）。
/// 收敛到常量类是为了避免前端拿到拼错的值静默不生效。
/// Success/Processing/Error/Warning/Default 对应 ProTable valueEnum 的 status 语义；
/// 其余为 antd 预设色值（对应 antd Tag 的 color）。
/// </summary>
public static class DataDictionaryTagTypes
{
    public const string Success = "success";
    public const string Processing = "processing";
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Default = "default";

    public const string Red = "red";
    public const string Orange = "orange";
    public const string Gold = "gold";
    public const string Green = "green";
    public const string Cyan = "cyan";
    public const string Blue = "blue";
    public const string Purple = "purple";

    public static readonly string[] All =
    [
        Success, Processing, Error, Warning, Default,
        Red, Orange, Gold, Green, Cyan, Blue, Purple
    ];

    public static bool IsValid(string? tagType)
    {
        return tagType == null || All.Contains(tagType);
    }
}
