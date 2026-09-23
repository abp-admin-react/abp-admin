using System;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 标注在枚举成员上，声明该成员同步为数据字典项时的标签颜色（T3.4 第 12 步）。
/// 取值收敛到 <see cref="DataDictionaryTagTypes"/> 的常量。
/// 由 EnumDataDictionarySyncDataSeedContributor 在种子阶段物化到 AppDataDictionaryItemMetas 表。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class DictionaryTagAttribute : Attribute
{
    public DictionaryTagAttribute(string tagType)
    {
        TagType = tagType;
    }

    public string TagType { get; }
}
