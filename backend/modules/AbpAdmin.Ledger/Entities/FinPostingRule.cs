using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>过账规则 FinPostingRules。定位键 (BizType, Phase) + Version。</summary>
public class FinPostingRule : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string BizType { get; set; } = default!;

    public string Phase { get; set; } = default!;

    public int Version { get; set; } = 1;

    public FinPostingRuleStatus Status { get; set; } = FinPostingRuleStatus.Approved;

    /// <summary>借贷行模板 JSON。</summary>
    public string TemplateJson { get; set; } = "[]";

    public string? Description { get; set; }

    protected FinPostingRule() { }

    public FinPostingRule(
        Guid id,
        string bizType,
        string phase,
        string templateJson,
        int version = 1,
        string? description = null,
        Guid? tenantId = null)
        : base(id)
    {
        BizType = bizType;
        Phase = phase;
        TemplateJson = templateJson;
        Version = version;
        Description = description;
        TenantId = tenantId;
        Status = FinPostingRuleStatus.Approved;
    }
}
