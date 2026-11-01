using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>对账运行 FinReconcileRuns。</summary>
public class FinReconcileRun : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string RunType { get; set; } = default!;

    public string? ScopeJson { get; set; }

    public string? DiffJson { get; set; }

    public bool HasDiff { get; set; }

    /// <summary>对账默认排除演示；true 表示本跑包含演示。</summary>
    public bool IncludeDemo { get; set; }

    protected FinReconcileRun() { }

    public FinReconcileRun(Guid id, string runType, Guid? tenantId = null)
        : base(id)
    {
        RunType = runType;
        TenantId = tenantId;
    }
}
