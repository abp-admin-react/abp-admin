using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>用户可用余额调账申请，审核完成后留存原始申请与记账关联。</summary>
public class ColBalanceAdjustment : BasicAggregateRoot<Guid>
{
    protected ColBalanceAdjustment()
    {
    }

    public ColBalanceAdjustment(Guid id, Guid requestId, Guid userId, CollectionCurrency currency,
        decimal amount, string reason, Guid applicantId, DateTime creationTime) : base(id)
    {
        RequestId = requestId;
        UserId = userId;
        Currency = currency;
        Amount = amount;
        Reason = reason;
        ApplicantId = applicantId;
        CreationTime = DatabaseTimestamp(creationTime);
    }

    public Guid RequestId { get; private set; }
    public Guid UserId { get; private set; }
    public CollectionCurrency Currency { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = default!;
    public CollectionBalanceAdjustmentStatus Status { get; private set; }
    public Guid ApplicantId { get; private set; }
    public Guid? ReviewerId { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }
    public Guid? BalanceTxnId { get; private set; }
    public Guid? LedgerVoucherId { get; private set; }
    public decimal? BalanceAfter { get; private set; }
    public DateTime CreationTime { get; private set; }

    public void Approve(Guid reviewerId, DateTime reviewedAt, Guid balanceTxnId, Guid ledgerVoucherId, decimal balanceAfter)
    {
        RequirePending();
        Status = CollectionBalanceAdjustmentStatus.Approved;
        ReviewerId = reviewerId;
        ReviewedAt = DatabaseTimestamp(reviewedAt);
        BalanceTxnId = balanceTxnId;
        LedgerVoucherId = ledgerVoucherId;
        BalanceAfter = balanceAfter;
    }

    public void Reject(Guid reviewerId, DateTime reviewedAt, string reason)
    {
        RequirePending();
        Status = CollectionBalanceAdjustmentStatus.Rejected;
        ReviewerId = reviewerId;
        ReviewedAt = DatabaseTimestamp(reviewedAt);
        ReviewNote = reason;
    }

    private void RequirePending()
    {
        if (Status != CollectionBalanceAdjustmentStatus.Pending)
        {
            throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.InvalidStatus);
        }
    }

    // PostgreSQL timestamps retain microseconds, so initial and replay responses share the same instant.
    private static DateTime DatabaseTimestamp(DateTime value) => value.AddTicks(-(value.Ticks % 10));
}
