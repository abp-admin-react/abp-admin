using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Entities;

/// <summary>
/// 人工调账 FinManualAdjusts（定稿 §5.1 五态）。
/// 自批闸门不在实体：应用服务按 Finance.Adjust.ApproveOwn 判定（缺权 → SelfApproveNotAllowed）。
/// 状态迁移只走本类方法；非法迁移一律 <see cref="FinanceErrorCodes.AdjustInvalidStatus"/>。
/// Submit 不校验 reason（可空草稿）；提交必填 reason 由 AppService.SubmitAsync 强制。
/// </summary>
public class FinManualAdjust : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid ApplicantId { get; private set; }

    public Guid? AuditorId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = default!;

    public Guid DebitAccountId { get; private set; }

    public FinOwnerType DebitOwnerType { get; private set; }

    public string DebitOwnerId { get; private set; } = "";

    public Guid CreditAccountId { get; private set; }

    public FinOwnerType CreditOwnerType { get; private set; }

    public string CreditOwnerId { get; private set; } = "";

    public string? Reason { get; private set; }

    public string? RejectReason { get; private set; }

    public FinManualAdjustStatus Status { get; private set; } = FinManualAdjustStatus.Draft;

    public DateTime? SubmittedAt { get; private set; }

    public DateTime? AuditedAt { get; private set; }

    public Guid? VoucherId { get; private set; }

    public Guid? ReversalVoucherId { get; private set; }

    protected FinManualAdjust() { }

    public FinManualAdjust(Guid id, Guid applicantId, FinManualAdjustLines lines, string? reason, Guid? tenantId)
        : base(id)
    {
        ApplicantId = applicantId;
        TenantId = tenantId;
        Apply(lines, reason);
        Status = FinManualAdjustStatus.Draft;
    }

    public void Update(FinManualAdjustLines lines, string? reason)
    {
        Require(FinManualAdjustStatus.Draft);
        Apply(lines, reason);
    }

    public void Submit(DateTime now)
    {
        Require(FinManualAdjustStatus.Draft);
        Status = FinManualAdjustStatus.Pending;
        SubmittedAt = now;
    }

    public void Reject(Guid auditorId, string? reason, DateTime now)
    {
        Require(FinManualAdjustStatus.Pending);
        AuditorId = auditorId;
        AuditedAt = now;
        RejectReason = reason;
        Status = FinManualAdjustStatus.Rejected;
    }

    /// <summary>Rejected → Draft。保留 RejectReason；清空 Auditor/Submitted/Audited。</summary>
    public void Reopen()
    {
        Require(FinManualAdjustStatus.Rejected);
        Status = FinManualAdjustStatus.Draft;
        AuditorId = null;
        AuditedAt = null;
        SubmittedAt = null;
        // RejectReason 故意保留，重提后仍可见
    }

    public void MarkPosted(Guid auditorId, Guid voucherId, DateTime now)
    {
        Require(FinManualAdjustStatus.Pending);
        AuditorId = auditorId;
        AuditedAt = now;
        VoucherId = voucherId;
        Status = FinManualAdjustStatus.Posted;
    }

    public void MarkReversed(Guid reversalVoucherId)
    {
        Require(FinManualAdjustStatus.Posted);
        ReversalVoucherId = reversalVoucherId;
        Status = FinManualAdjustStatus.Reversed;
    }

    public void EnsureDeletable() => Require(FinManualAdjustStatus.Draft);

    public void Require(FinManualAdjustStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessException(FinanceErrorCodes.AdjustInvalidStatus)
                .WithData("Status", Status.ToString())
                .WithData("Expected", expected.ToString());
        }
    }

    private void Apply(FinManualAdjustLines lines, string? reason)
    {
        Amount = lines.Amount;
        Currency = lines.Currency;
        DebitAccountId = lines.DebitAccountId;
        DebitOwnerType = lines.DebitOwnerType;
        DebitOwnerId = lines.DebitOwnerId ?? "";
        CreditAccountId = lines.CreditAccountId;
        CreditOwnerType = lines.CreditOwnerType;
        CreditOwnerId = lines.CreditOwnerId ?? "";
        Reason = reason;
    }
}

/// <summary>调账借贷内容（单借单贷，金额相等即平衡）。</summary>
public sealed record FinManualAdjustLines(
    decimal Amount,
    string Currency,
    Guid DebitAccountId,
    FinOwnerType DebitOwnerType,
    string? DebitOwnerId,
    Guid CreditAccountId,
    FinOwnerType CreditOwnerType,
    string? CreditOwnerId);
