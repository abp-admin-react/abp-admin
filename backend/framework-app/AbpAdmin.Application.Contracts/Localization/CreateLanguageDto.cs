using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Localization;

public class CreateLanguageDto
{
    [Required]
    [StringLength(LanguageConsts.MaxCultureNameLength)]
    public string CultureName { get; set; } = string.Empty;

    [Required]
    [StringLength(LanguageConsts.MaxUiCultureNameLength)]
    public string UiCultureName { get; set; } = string.Empty;

    [Required]
    [StringLength(LanguageConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(LanguageConsts.MaxFlagIconLength)]
    public string? FlagIcon { get; set; }

    public bool IsEnabled { get; set; } = true;
}
