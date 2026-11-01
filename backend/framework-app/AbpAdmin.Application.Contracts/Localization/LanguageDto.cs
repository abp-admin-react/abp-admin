using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Localization;

public class LanguageDto : AuditedEntityDto<Guid>
{
    public string CultureName { get; set; } = string.Empty;

    public string UiCultureName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? FlagIcon { get; set; }

    public bool IsEnabled { get; set; }

    public bool IsDefault { get; set; }
}
