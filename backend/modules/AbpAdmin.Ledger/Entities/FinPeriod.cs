using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>会计期间 FinPeriods。本阶段仅维护 Open 当前期。</summary>
public class FinPeriod : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public FinPeriodStatus Status { get; set; } = FinPeriodStatus.Open;

    protected FinPeriod() { }

    public FinPeriod(Guid id, int year, int month, Guid? tenantId = null)
        : base(id)
    {
        Year = year;
        Month = month;
        TenantId = tenantId;
        Status = FinPeriodStatus.Open;
    }
}
