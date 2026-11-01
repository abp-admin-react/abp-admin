using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>
/// 凭证头 FinVouchers。
/// 幂等 UK：(TenantId, BizType, BizRefId, Phase, BatchKey)；BatchKey 默认 ""。
/// </summary>
public class FinVoucher : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string BizType { get; set; } = default!;

    public string BizRefId { get; set; } = default!;

    public string Phase { get; set; } = default!;

    /// <summary>多笔 recognize 批次键；单次过账为 ""。</summary>
    public string BatchKey { get; set; } = "";

    public int RuleVersion { get; set; }

    public Guid? PeriodId { get; set; }

    public FinVoucherStatus Status { get; set; } = FinVoucherStatus.Posted;

    public Guid? ReversalOf { get; set; }

    public Guid? AgentId { get; set; }

    public bool IsDemo { get; set; }

    /// <summary>过账时间（UTC，与系统 Clock 无关）。对账按此字段筛区间。</summary>
    public DateTime PostedTime { get; set; }

    public string? Summary { get; set; }

    public string Currency { get; set; } = default!;

    public decimal Amount { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>幂等内容指纹（金额/币种/用户/代理/对手账户的 SHA-256）；同键不同指纹 = 冲突。</summary>
    public string Fingerprint { get; set; } = "";

    protected FinVoucher() { }

    public FinVoucher(
        Guid id,
        string bizType,
        string bizRefId,
        string phase,
        string currency,
        decimal amount,
        string? batchKey = null,
        int ruleVersion = 1,
        Guid? agentId = null,
        bool isDemo = false,
        string? summary = null,
        Guid? tenantId = null)
        : base(id)
    {
        BizType = bizType;
        BizRefId = bizRefId;
        Phase = phase;
        BatchKey = batchKey ?? "";
        Currency = currency;
        Amount = amount;
        RuleVersion = ruleVersion;
        AgentId = agentId;
        IsDemo = isDemo;
        Summary = summary;
        TenantId = tenantId;
        Status = FinVoucherStatus.Posted;
    }
}
