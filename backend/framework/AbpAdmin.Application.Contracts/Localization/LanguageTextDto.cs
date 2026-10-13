using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Localization;

/// <summary>
/// 语言文本列表行：静态基线行（从未被覆盖）与覆盖行统一在一个列表里。
/// 静态基线行没有数据库实体，Id 为 null——所以基类从 AuditedEntityDto&lt;Guid&gt; 降为 EntityDto。
/// </summary>
public class LanguageTextDto : EntityDto
{
    /// <summary>覆盖行的实体 Id；静态基线行（无覆盖）为 null。</summary>
    public Guid? Id { get; set; }

    public string ResourceName { get; set; } = string.Empty;

    public string CultureName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 目标文化生效值（合并口径）：有生效覆盖（本层或 host——host 覆盖对租户生效）取覆盖值
    /// （空串=显式未翻译），否则取静态基线值，两者都没有为空串。
    /// 静态基线沿目标文化父链展开，刻意不含框架 DefaultCulture 回退（与运行时管线的唯一差异，
    /// 见 GetListAsync / StaticLocalizationTextProvider——链上缺失的 key 显示空串以便漏译可见）；
    /// 与 IsOverridden（仅本层）刻意区分，见该属性注释。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 基准文化对照值（含父文化回退），对应 Pro 语言文本页的 BaseCulture 列。
    /// 与 Value 同一合并口径（本层覆盖 &gt; host 覆盖 &gt; 静态基线）——即基准文化的生效值，
    /// 基准文化被覆盖时对照列不失真。与目标列的唯一差异：基准列的静态回退链并入
    /// 资源默认文化（Pro 同款），目标列不并入。
    /// BaseCultureName 未传、key 不在基准文化里时为 null（前端显示为空）。
    /// </summary>
    public string? BaseValue { get; set; }

    /// <summary>
    /// 当前上下文（host 或本租户）是否存在数据库覆盖行。
    /// 「恢复默认」只删除当前上下文那一层覆盖（RestoreToDefaultAsync / Pro 的 per-context 语义），
    /// 前端据此决定按钮是否可点：只有本字段为 true 时，恢复才会真正删除覆盖行。
    /// 注意两点边界：①与 Value 的口径区分——Value 是合并口径（本层覆盖 &gt; host 覆盖 &gt; 静态 json），
    /// 租户看到 host 覆盖的值时本字段为 false；②本字段为 true 时恢复删行必然发生，
    /// 但若覆盖值恰好等于下层提供值，显示值可能不变（存储变化 ≠ 显示变化）。
    /// </summary>
    public bool IsOverridden { get; set; }
}
