using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Localization;

/// <summary>
/// 语言文本列表查询。Sorting 走白名单内存排序：
/// 支持字段 ResourceName/Name/Value/BaseValue（可加 asc/desc，逗号分隔多字段，Ordinal 比较），
/// 未知字段忽略，全部未知时回退默认 ResourceName→Name 序。
/// </summary>
public class GetLanguageTextsInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 资源名，可选：不传 = 跨全部注册资源列出（Pro 同款；表格以 ResourceName 列区分来源）。
    /// </summary>
    [StringLength(LanguageTextConsts.MaxResourceNameLength)]
    public string? ResourceName { get; set; }

    /// <summary>
    /// 目标文化，必选（页面默认选中第二个启用语言，否则第一个——Pro 同款）。
    /// 静态基线与覆盖行的取值/回退都沿它展开。
    /// </summary>
    [Required]
    [StringLength(LanguageTextConsts.MaxCultureNameLength)]
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// 基准文化对照（BaseValue 列），可选；含父文化回退，不传则 BaseValue 为 null。
    /// </summary>
    [StringLength(LanguageTextConsts.MaxCultureNameLength)]
    public string? BaseCultureName { get; set; }

    /// <summary>模糊过滤：匹配 key 或生效值（不区分大小写）。</summary>
    public string? Filter { get; set; }

    /// <summary>true = 只看未翻译（生效值为空的行，含从未覆盖的静态 key）。</summary>
    public bool OnlyEmpty { get; set; }
}
