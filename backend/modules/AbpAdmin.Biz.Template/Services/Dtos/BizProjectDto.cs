using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Biz.Template.Services.Dtos;

public class BizProjectDto : AuditedEntityDto<Guid>
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
