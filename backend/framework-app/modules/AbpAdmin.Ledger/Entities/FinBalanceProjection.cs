using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>余额投影 FinBalanceProjections。</summary>
public class FinBalanceProjection : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid SubAccountId { get; set; }

    public decimal Balance { get; set; }

    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; }

    protected FinBalanceProjection() { }

    public FinBalanceProjection(Guid id, Guid subAccountId, Guid? tenantId = null)
        : base(id)
    {
        SubAccountId = subAccountId;
        TenantId = tenantId;
        UpdatedAt = DateTime.UtcNow;
    }
}
