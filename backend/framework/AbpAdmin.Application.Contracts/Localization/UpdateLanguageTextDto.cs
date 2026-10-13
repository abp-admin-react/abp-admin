using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Localization;

public class UpdateLanguageTextDto
{
    [Required]
    [StringLength(LanguageTextConsts.MaxResourceNameLength)]
    public string ResourceName { get; set; } = string.Empty;

    [Required]
    [StringLength(LanguageTextConsts.MaxCultureNameLength)]
    public string CultureName { get; set; } = string.Empty;

    [Required]
    [StringLength(LanguageTextConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 本地化值。允许空串（AllowEmptyStrings）——与实体 LanguageText 的"空串 = 显式未翻译"
    /// 语义对齐（用户清空文本框保存 = 标记未翻译，而非参数错误）。
    /// </summary>
    [Required(AllowEmptyStrings = true)]
    [StringLength(LanguageTextConsts.MaxValueLength)]
    public string Value { get; set; } = string.Empty;
}
