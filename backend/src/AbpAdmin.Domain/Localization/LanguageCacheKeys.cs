using System;

namespace AbpAdmin.Localization;

public static class LanguageCacheKeys
{
    public const string AllLanguages = "AbpAdmin:Languages:All";

    /// <summary>
    /// 语言文本合并缓存 key。version 是资源+文化维度的版本值（见 <see cref="LanguageTextVersion"/>）：
    /// 写操作换一个新版本值，所有租户的旧合并条目即整体作废。
    /// （host 行会被合并进每个租户的缓存值，按租户逐 key 清理无法枚举租户集合，版本号是唯一可行路径。）
    /// </summary>
    public static string LanguageTexts(Guid? tenantId, string resourceName, string cultureName, string version)
        => $"AbpAdmin:LanguageTexts:{tenantId?.ToString() ?? "host"}:{resourceName}:{cultureName}:v{version}";

    /// <summary>
    /// 资源+文化的文本版本号 key（缓存值为任意唯一字符串，写入口每次替换新值）。
    /// </summary>
    public static string LanguageTextVersion(string resourceName, string cultureName)
        => $"AbpAdmin:LanguageTexts:Version:{resourceName}:{cultureName}";
}
