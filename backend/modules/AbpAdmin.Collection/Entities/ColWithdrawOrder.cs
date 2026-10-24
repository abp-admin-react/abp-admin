using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 提现单（ColWithdrawOrders，提现 M2）：用户把稳定币余额提到链上地址。
/// 提交即冻结净额+手续费（ColBalances 两桶）；审核通过冻结出账（WithdrawSettle）；
/// 广播失败终态退款回可用（WithdrawRefund）。账本同步过账（BizType=withdraw，
/// phase = freeze/unfreeze/approve/fee-take/payout/refund），与余额操作同事务。
/// 状态迁移全部 CAS 条件 UPDATE；金额口径：Amount=总额、Fee=手续费、ActualAmount=净出账。
/// </summary>
public class ColWithdrawOrder : BasicAggregateRoot<Guid>
{
    public ColWithdrawOrder(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColWithdrawOrder()
    {
    }
#pragma warning restore CS8618

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>提现网络（当前仅 BSC，ColNetwork.Name）。</summary>
    public string NetworkName { get; set; } = default!;

    /// <summary>收款地址（0x + 40 位十六进制，提交时校验；黑名单地址在提交与管理端双卡）。</summary>
    public string ToAddress { get; set; } = default!;

    /// <summary>提现总额（用户申请的金额，手续费从中扣）。</summary>
    public decimal Amount { get; set; }

    /// <summary>手续费（费率快照计算，8 位截断）。</summary>
    public decimal Fee { get; set; }

    /// <summary>净出账 = Amount − Fee（链上实际打款金额）。</summary>
    public decimal ActualAmount { get; set; }

    /// <summary>提交时的费率快照（对账解释 Fee 用）。</summary>
    public decimal FeeRate { get; set; }

    public WithdrawStatus Status { get; set; }

    /// <summary>提现交易哈希（小写）；重试清哈希回 Processing。</summary>
    public string? TxHash { get; set; }

    /// <summary>提现交易 nonce（热钱包 pending nonce；重播双花门与替换重签用）。</summary>
    public long? NonceUsed { get; set; }

    /// <summary>广播时刻（Broadcasting 超时恢复的计时起点）。</summary>
    public DateTime? BroadcastAt { get; set; }

    public int RetryCount { get; set; }

    /// <summary>失败/拒绝原因（运维可读中文）。</summary>
    public string? FailReason { get; set; }

    public Guid? AuditUserId { get; set; }

    public string? AuditRemark { get; set; }

    public DateTime? AuditTime { get; set; }

    /// <summary>打款确认完成时刻（UTC）。</summary>
    public DateTime? CompleteTime { get; set; }

    /// <summary>状态变化时间：卡单恢复/冷却判据。</summary>
    public DateTime StatusChangedAt { get; set; }

    public DateTime CreationTime { get; set; }
}
