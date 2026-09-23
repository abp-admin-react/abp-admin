using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.DataScopes;

public class DataScopeDemoDto : AuditedEntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public string Name { get; set; } = default!;
}
