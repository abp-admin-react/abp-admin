using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Biz.Template.Services.Dtos;

public class BizDataScopeDemoDto : AuditedEntityDto<Guid>
{
    public string Name { get; set; } = default!;

    public Guid? OrganizationUnitId { get; set; }
}
