using System;
using AbpAdmin.DataScopes;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Biz.Template.Entities;

/// <summary>
/// 数据权限示例实体（业务模块侧）：实现框架的 <see cref="IHasDataScope"/> 标记接口，
/// 配合 <see cref="Data.BizTemplateDbContext"/> 里的数据范围全局筛选器，即自动纳入
/// 「按角色数据范围过滤」——演示业务模块如何消费框架数据权限基建。
/// 语义与框架 DataScopeDemo 对齐：OrganizationUnitId 为 null 的行只对 All 可见（fail-closed）；
/// SelfOnly 与机构范围是「或」关系。
/// </summary>
public class BizDataScopeDemo : AuditedAggregateRoot<Guid>, IHasDataScope, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid? OrganizationUnitId { get; protected set; }

    public virtual string Name { get; protected set; } = default!;

    protected BizDataScopeDemo()
    {
    }

    public BizDataScopeDemo(Guid id, string name, Guid? organizationUnitId = null)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: 128);
        OrganizationUnitId = organizationUnitId;
    }
}
