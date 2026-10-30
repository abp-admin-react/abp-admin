using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Editions;

public class EditionDto : EntityDto<Guid>
{
    public string DisplayName { get; set; } = default!;
}

public class CreateEditionDto
{
    [Required]
    [StringLength(EditionConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = default!;
}

public class UpdateEditionDto : CreateEditionDto
{
}

/// <summary>
/// 删除版本时的租户迁移选项。
/// </summary>
public class DeleteEditionInput
{
    /// <summary>
    /// 迁移目标版本。留空表示清空这些租户的版本分配（EditionId 设为 null）。
    /// </summary>
    public Guid? MoveTenantsToEditionId { get; set; }
}
