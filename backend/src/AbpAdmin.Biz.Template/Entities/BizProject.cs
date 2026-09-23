using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AbpAdmin.Biz.Template.Entities;

/// <summary>
/// 样例聚合根：演示业务模块实体的自包含写法。
/// 约束：模块内的表只归本模块的迁移所有，不建跨上下文外键/导航，
/// 跨模块关联用 Id + 应用层组合（需要租户隔离的实体加 IMultiTenant）。
/// </summary>
public class BizProject : AuditedAggregateRoot<Guid>
{
    public string Name { get; protected set; } = default!;

    public string? Description { get; protected set; }

    public bool IsActive { get; protected set; }

    protected BizProject()
    {
    }

    public BizProject(Guid id, string name, string? description = null, bool isActive = true)
        : base(id)
    {
        Name = name;
        Description = description;
        IsActive = isActive;
    }

    public void SetName(string name)
    {
        Name = name;
    }

    public void SetDescription(string? description)
    {
        Description = description;
    }

    public void SetIsActive(bool isActive)
    {
        IsActive = isActive;
    }
}
