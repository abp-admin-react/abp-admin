using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 稳定币余额流水（ColBalanceTxns，计划第 5.5 节）：只追加，不更新——唯一例外是
/// 扣款行的 ReversedAt（回冲打点，带 WHERE ReversedAt IS NULL 的条件更新）。
/// Amount 带符号：充值/回冲为正，扣款为负。IdempotencyKey 唯一：充值行是进款单 id（M5），
/// 扣款行每次新生成——业务级幂等由 (BusinessType, BusinessId) 上的部分唯一索引承担
///（谓词 Action = 'BusinessDebit' AND ReversedAt IS NULL，回冲后再扣用新键）。
/// Seq 是 PG 自增、插入时分配：充值流水的单调序号，专供 ListCreditedTxnsAsync，
/// 不能单靠它防漏（未提交的行拿不到序号），防漏靠 7 天重扫（计划第 3.1 节）。
/// </summary>
public class ColBalanceTxn : BasicAggregateRoot<Guid>
{
    public ColBalanceTxn(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColBalanceTxn()
    {
    }
#pragma warning restore CS8618

    /// <summary>bigint identity，PG 端分配（EF 配置 UseIdentityColumn）。</summary>
    public long Seq { get; private set; }

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>动作类型：BalanceActions 常量（契约先行，落库为字符串）。</summary>
    public string Action { get; set; } = default!;

    /// <summary>带符号金额，numeric(28,18)。</summary>
    public decimal Amount { get; set; }

    /// <summary>本行落账后的余额（同事务内写入）。</summary>
    public decimal BalanceAfter { get; set; }

    public string IdempotencyKey { get; set; } = default!;

    /// <summary>扣款行：业务单类型（订单模块自己的常量）。充值行：TopUp。回冲行复制被回冲行的业务单。</summary>
    public string? BusinessType { get; set; }

    public Guid? BusinessId { get; set; }

    /// <summary>回冲行指向被回冲的扣款流水。</summary>
    public Guid? RefTxnId { get; set; }

    /// <summary>扣款行的回冲打点；回冲的条件更新带 WHERE ReversedAt IS NULL。</summary>
    public DateTime? ReversedAt { get; set; }

    public DateTime CreationTime { get; set; }
}
