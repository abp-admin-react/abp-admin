using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>分录 FinEntries（append-only）。</summary>
public class FinEntry : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid VoucherId { get; set; }

    public Guid SubAccountId { get; set; }

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    public decimal? BalanceAfter { get; set; }

    /// <summary>jsonb 维度（product/channel/order 等）。</summary>
    public string? Dimensions { get; set; }

    public int Seq { get; set; }

    protected FinEntry() { }

    public FinEntry(
        Guid id,
        Guid voucherId,
        Guid subAccountId,
        decimal debit,
        decimal credit,
        int seq,
        string? dimensions = null,
        Guid? tenantId = null)
        : base(id)
    {
        VoucherId = voucherId;
        SubAccountId = subAccountId;
        Debit = debit;
        Credit = credit;
        Seq = seq;
        Dimensions = dimensions;
        TenantId = tenantId;
    }
}
