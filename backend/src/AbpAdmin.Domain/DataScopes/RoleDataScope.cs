using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色数据范围配置。一个角色一条记录，键用 <see cref="RoleName"/>（对应 <c>IdentityRole.Name</c>），不用 RoleId。
/// <para>注意：本实体绝不能实现 <see cref="IHasDataScope"/>——它是数据范围解析器自己的输入。</para>
/// </summary>
public class RoleDataScope : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>
    /// 角色名，对应 <c>IdentityRole.Name</c>。同一 <see cref="RoleName"/> 唯一。
    /// </summary>
    public virtual string RoleName { get; protected set; } = default!;

    /// <summary>
    /// 数据范围类型。
    /// </summary>
    public virtual DataScopeTypeEnum ScopeType { get; protected set; }

    /// <summary>
    /// 自定义组织单元集合（<see cref="DataScopeTypeEnum.Custom"/> 时使用）。
    /// </summary>
    public virtual ICollection<RoleDataScopeOrganizationUnit> CustomOrganizationUnits { get; protected set; } = new List<RoleDataScopeOrganizationUnit>();

    protected RoleDataScope() { }

    public RoleDataScope(Guid id, string roleName, DataScopeTypeEnum scopeType, Guid? tenantId = null)
        : base(id)
    {
        RoleName = Check.NotNullOrWhiteSpace(roleName, nameof(roleName), maxLength: RoleDataScopeConsts.MaxRoleNameLength);
        ScopeType = scopeType;
        TenantId = tenantId;
    }

    public void SetScopeType(DataScopeTypeEnum scopeType)
    {
        ScopeType = scopeType;
    }
}
