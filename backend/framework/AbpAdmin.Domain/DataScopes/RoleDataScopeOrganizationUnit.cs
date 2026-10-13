using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色数据范围的自定义组织单元（<see cref="DataScopeTypeEnum.Custom"/> 时使用）。
/// 复合主键 {RoleDataScopeId, OrganizationUnitId}。
/// </summary>
public class RoleDataScopeOrganizationUnit : Entity
{
    public virtual Guid RoleDataScopeId { get; protected set; }

    public virtual Guid OrganizationUnitId { get; protected set; }

    protected RoleDataScopeOrganizationUnit() { }

    public RoleDataScopeOrganizationUnit(Guid roleDataScopeId, Guid organizationUnitId)
    {
        RoleDataScopeId = roleDataScopeId;
        OrganizationUnitId = organizationUnitId;
    }

    public override object[] GetKeys()
    {
        return new object[] { RoleDataScopeId, OrganizationUnitId };
    }
}
