using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Localization;

public class UpdateLanguageDto
{
    [Required]
    [StringLength(LanguageConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(LanguageConsts.MaxFlagIconLength)]
    public string? FlagIcon { get; set; }

    public bool IsEnabled { get; set; }
}
