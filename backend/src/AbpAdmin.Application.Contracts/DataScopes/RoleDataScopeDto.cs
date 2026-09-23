using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.DataScopes;

public class RoleDataScopeDto : EntityDto<Guid>
{
    public Guid? TenantId { get; set; }
    public string RoleName { get; set; } = default!;
    public DataScopeTypeEnum ScopeType { get; set; }
    public List<Guid> CustomOrganizationUnitIds { get; set; } = new();
}
