using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 归集任务（计划第 5.6 节）：把用户地址上的代币转到金库。同址同币排队任务可合并
///（部分唯一索引保证同址同币同时最多一个未完成任务）；同地址已有 Fueling/Sweeping 时
/// 另一种币不能再占（地址级互斥的部分唯一索引）。冷却看 StatusChangedAt（只在状态变化时写）。
/// 金库地址取自 ColToken；金库私钥系统不保存。
/// </summary>
public class ColSweepTask : BasicAggregateRoot<Guid>
{
    public ColSweepTask(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColSweepTask()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public Guid TokenId { get; set; }

    public Guid UserId { get; set; }

    public Guid AddressId { get; set; }

    /// <summary>用户充值地址（打油与归集的 from）。</summary>
    public string Address { get; set; } = default!;

    /// <summary>HD 派生索引（≥2；签名时按 MnemonicCipher 现推私钥）。</summary>
    public int AddressIndex { get; set; }

    /// <summary>激活（Queued→下一步）时按已链接进款单求和（每轮有取出上限）。</summary>
    public decimal Amount { get; set; }

    public SweepTaskStatus Status { get; set; }

    /// <summary>Failed 时：可再试（冷却后自动拉回）或仅人工（打油超上限、换算失败）。</summary>
    public bool Retryable { get; set; }

    public string? FailReason { get; set; }

    /// <summary>代币归集交易哈希（小写）。确认数不够保持 Sweeping 和原哈希，不重签；
    /// 宽限后节点仍不认才允许重签，同地址出现替换哈希时跟随新哈希。</summary>
    public string? TxHash { get; set; }

    /// <summary>归集交易的 nonce（重签复用同一 nonce 替换）。</summary>
    public long? NonceUsed { get; set; }

    /// <summary>归集交易所在块（确认数计算基准）。</summary>
    public long? TxBlockNumber { get; set; }

    /// <summary>打油的 BNB 交易哈希（排障用）。</summary>
    public string? GasTxHash { get; set; }

    /// <summary>
    /// 当前在途归集交易的广播时刻（重签宽限的计时起点；清哈希重签时更新）。
    /// 不与 StatusChangedAt 复用——冷却与宽限是两个时钟，复用会让激活/冷却重置宽限。
    /// </summary>
    public DateTime? BroadcastAt { get; set; }

    /// <summary>状态变化时间：冷却判据，不用 LastModificationTime。</summary>
    public DateTime StatusChangedAt { get; set; }

    public DateTime CreationTime { get; set; }
}
