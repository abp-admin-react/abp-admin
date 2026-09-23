using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 字典项展示元数据（T3.4 第 12 步，表 AppDataDictionaryItemMetas）。
/// 模块的 DataDictionaryItem 没有任何可扩展字段（复合主键 (DataDictionaryId, Code)、
/// 无 Id、不实现 IHasExtraProperties），标签颜色与排序只能旁挂自己的表。
/// 与模块表是逻辑关联，不建外键：模块表主键是复合键，且给别人模块的表加外键
/// 会让模块升级时的迁移打结。孤儿行由枚举同步种子（EnumDataDictionarySyncDataSeedContributor）顺带清理。
/// 命名口径：DataDictionary 前缀跟的是模块实体 DataDictionary/DataDictionaryItem，
/// 与 T1.5 行级数据权限的 DataScope 系列没有任何关系。
/// </summary>
public class DataDictionaryItemMeta : AuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>所属字典编码（逻辑关联 DataDictionary.Code，同租户内唯一）。</summary>
    public virtual string DictionaryCode { get; protected set; } = default!;

    /// <summary>字典项编码（逻辑关联 DataDictionaryItem.Code）。</summary>
    public virtual string ItemCode { get; protected set; } = default!;

    /// <summary>antd 标签色标识，取值见 <see cref="DataDictionaryTagTypes"/>。null 表示无颜色。</summary>
    public virtual string? TagType { get; protected set; }

    /// <summary>
    /// 显示顺序。枚举字典由同步种子写入成员声明顺序；手工字典项由管理页维护。
    /// 不要按 Code 排序：枚举字典的 Code 是数值字符串，字符串序下 "10" 会排在 "2" 前面。
    /// </summary>
    public virtual int Order { get; protected set; }

    protected DataDictionaryItemMeta()
    {
    }

    public DataDictionaryItemMeta(
        Guid id,
        Guid? tenantId,
        string dictionaryCode,
        string itemCode,
        string? tagType,
        int order)
        : base(id)
    {
        TenantId = tenantId;
        DictionaryCode = Check.NotNullOrWhiteSpace(dictionaryCode, nameof(dictionaryCode), DataDictionaryItemMetaConsts.MaxDictionaryCodeLength);
        ItemCode = Check.NotNullOrWhiteSpace(itemCode, nameof(itemCode), DataDictionaryItemMetaConsts.MaxItemCodeLength);
        TagType = tagType == null ? null : Check.Length(tagType, nameof(tagType), DataDictionaryItemMetaConsts.MaxTagTypeLength);
        Order = order;
    }

    public virtual void Update(string? tagType, int order)
    {
        TagType = tagType == null ? null : Check.Length(tagType, nameof(tagType), DataDictionaryItemMetaConsts.MaxTagTypeLength);
        Order = order;
    }
}
