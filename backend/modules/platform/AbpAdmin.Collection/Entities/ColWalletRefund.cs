using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 一笔原扣款上的商业退款或待核实占用。同一扣款下 RequestId 唯一。
/// 核实只确认已经发生的退付，不再给钱包入账。
/// </summary>
public class ColWalletRefund : BasicAggregateRoot<Guid>
{
    public ColWalletRefund(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColWalletRefund()
    {
    }
#pragma warning restore CS8618

    public Guid OriginalDebitTxnId { get; set; }

    public string RequestId { get; set; } = default!;

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    public decimal Amount { get; set; }

    public WalletRefundStatus Status { get; set; }

    /// <summary>成功退回钱包时的余额流水。待核实和已核实外部退付为空。</summary>
    public Guid? BalanceTxnId { get; set; }

    public DateTime CreationTime { get; set; }
}

public enum WalletRefundStatus
{
    PendingVerification = 0,
    VerifiedExternal = 1,
    Succeeded = 2,
}
