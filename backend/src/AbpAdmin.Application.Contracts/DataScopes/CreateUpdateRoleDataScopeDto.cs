using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.DataScopes;

public class CreateUpdateRoleDataScopeDto
{
    [Required]
    [StringLength(RoleDataScopeConsts.MaxRoleNameLength)]
    public string RoleName { get; set; } = default!;

    [Required]
    public DataScopeTypeEnum ScopeType { get; set; }

    /// <summary>
    /// 自定义组织单元集合（<see cref="DataScopeTypeEnum.Custom"/> 时必填）。
    /// </summary>
    public List<Guid> CustomOrganizationUnitIds { get; set; } = new();
}
