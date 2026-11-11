using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.DataScopes;

/// <summary>仅测试程序集使用的实体，用于直接验证框架 DbContext 的数据范围能力。</summary>
public class FrameworkDataScopeRecord : AuditedAggregateRoot<Guid>, IHasDataScope, IMultiTenant
{
    public Guid? TenantId { get; protected set; }
    public Guid? OrganizationUnitId { get; protected set; }
    public string Name { get; protected set; } = string.Empty;

    protected FrameworkDataScopeRecord() { }

    public FrameworkDataScopeRecord(Guid id, string name, Guid? organizationUnitId = null) : base(id)
    {
        Name = name;
        OrganizationUnitId = organizationUnitId;
    }
}
