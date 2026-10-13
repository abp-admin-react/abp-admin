using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据权限演示实体（本任务验收夹具，不是业务功能）。
/// 实现 <see cref="IHasDataScope"/> 即自动纳入数据范围过滤。
/// 第 2 批第一个真实业务实体落地后可评估是否删除。
/// </summary>
public class DataScopeDemo : AuditedAggregateRoot<Guid>, IHasDataScope, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid? OrganizationUnitId { get; protected set; }

    public virtual string Name { get; protected set; } = default!;

    protected DataScopeDemo() { }

    public DataScopeDemo(Guid id, string name, Guid? organizationUnitId = null) : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: 128);
        OrganizationUnitId = organizationUnitId;
    }
}
