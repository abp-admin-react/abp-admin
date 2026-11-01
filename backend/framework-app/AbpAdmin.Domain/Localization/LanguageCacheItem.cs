using System;
using System.Collections.Generic;

namespace AbpAdmin.Localization;

[Serializable]
public class LanguageCacheItem
{
    public List<LanguageInfo> Languages { get; set; } = new();
}

[Serializable]
public class LanguageInfo
{
    public string CultureName { get; set; } = string.Empty;
    public string UiCultureName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? FlagIcon { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
}
