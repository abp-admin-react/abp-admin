using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

public enum ChainTxPurpose
{
    SweepFuel = 1,
    SweepTransfer = 2,
    WithdrawPayout = 3,
    SponsorHeadSelfTransfer = 4,
}

public enum ChainTxAttemptStatus
{
    Signed = 0,
    Broadcasted = 1,
    Confirmed = 2,
    Reverted = 3,
    Replaced = 4,
    Rejected = 5,
}

/// <summary>
/// 链上交易尝试审计轨迹：每次打油、归集、提现出款与 Sponsor 队头清理在广播前持久化签名元数据，
/// 记录发送方、Nonce、抬价档位对应 gasPrice 与状态迁移，防止节点丢失或负载漂移导致重复出款/重复打油。
/// </summary>
public class ColChainTxAttempt : BasicAggregateRoot<Guid>
{
    public ColChainTxAttempt(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColChainTxAttempt()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public ChainTxPurpose Purpose { get; set; }

    /// <summary>关联业务单 ID（SweepTaskId 或 WithdrawOrderId）；Sponsor 自转清理为空。</summary>
    public Guid? RefId { get; set; }

    public string SenderAddress { get; set; } = default!;

    public string ToAddress { get; set; } = default!;

    public long Nonce { get; set; }

    public int AttemptNo { get; set; }

    public string TxHash { get; set; } = default!;

    public string GasPriceWei { get; set; } = default!;

    public int GasLimit { get; set; }

    /// <summary>广播前保留原始已签名交易，进入终态（Confirmed/Reverted/Replaced/Rejected）时清空。</summary>
    public string? RawSignedTx { get; set; }

    public ChainTxAttemptStatus Status { get; set; }

    public DateTime BroadcastAt { get; set; }

    public DateTime CreationTime { get; set; }
}
