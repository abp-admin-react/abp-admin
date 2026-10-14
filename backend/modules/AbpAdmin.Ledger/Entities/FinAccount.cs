using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>科目表 FinAccounts。</summary>
public class FinAccount : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string Code { get; set; } = default!;

    public string Name { get; set; } = default!;

    public FinAccountType Type { get; set; }

    public Guid? ParentId { get; set; }

    public FinNormalSide NormalSide { get; set; }

    public bool AllowNegative { get; set; }

    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    protected FinAccount() { }

    public FinAccount(
        Guid id,
        string code,
        string name,
        FinAccountType type,
        FinNormalSide normalSide,
        bool isSystem = true,
        bool allowNegative = false,
        Guid? parentId = null,
        Guid? tenantId = null)
        : base(id)
    {
        Code = code;
        Name = name;
        Type = type;
        NormalSide = normalSide;
        IsSystem = isSystem;
        AllowNegative = allowNegative;
        ParentId = parentId;
        TenantId = tenantId;
        IsActive = true;
    }
}
