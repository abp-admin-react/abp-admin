using EasyAbp.Abp.DataDictionary;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// DataDictionaryItemMeta 的字段长度常量。
/// 字典/字典项编码长度与模块常量对齐（模块表也是这个上限，对齐后逻辑关联不会因长度截断而错位）。
/// </summary>
public static class DataDictionaryItemMetaConsts
{
    public const int MaxDictionaryCodeLength = DataDictionaryConsts.MaxCodeLength;

    public const int MaxItemCodeLength = DataDictionaryItemConsts.MaxCodeLength;

    public const int MaxTagTypeLength = 32;
}
