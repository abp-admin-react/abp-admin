using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>子账户 FinSubAccounts。UK (Tenant,Account,OwnerType,OwnerId,Currency,Bucket)。</summary>
public class FinSubAccount : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid AccountId { get; set; }

    public FinOwnerType OwnerType { get; set; }

    public string OwnerId { get; set; } = "";

    public string Currency { get; set; } = default!;

    public string Bucket { get; set; } = "";

    protected FinSubAccount() { }

    public FinSubAccount(
        Guid id,
        Guid accountId,
        FinOwnerType ownerType,
        string ownerId,
        string currency,
        string? bucket = null,
        Guid? tenantId = null)
        : base(id)
    {
        AccountId = accountId;
        OwnerType = ownerType;
        OwnerId = ownerId ?? "";
        Currency = currency;
        Bucket = bucket ?? "";
        TenantId = tenantId;
    }
}
