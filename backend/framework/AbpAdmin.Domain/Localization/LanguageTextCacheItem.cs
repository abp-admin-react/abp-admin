using System;
using System.Collections.Generic;

namespace AbpAdmin.Localization;

[Serializable]
public class LanguageTextCacheItem
{
    public Dictionary<string, string> Texts { get; set; } = new();
}
