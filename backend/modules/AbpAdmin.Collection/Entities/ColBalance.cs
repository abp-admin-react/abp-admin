using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 用户稳定币余额行（ColBalances，计划第 5.5 节）：唯一 (UserId, Currency)，非负。
/// 提现 M1 起分两桶：<see cref="Balance"/> = 可用（充值/退款入此桶，扣款只看此桶），
/// <see cref="Frozen"/> = 提现冻结；合计 = Balance + Frozen（金库核对按合计）。
/// 对齐账本科目 2201 可用 / 2202 冻结。
/// 扣款不依赖版本列做条件（「余额足够才减」的那条 SQL 自己保证）；
/// Version 列只留给管理端并发编辑，避免两套条件各写各的——M2 不启用 EF 并发配置。
/// 无租户列，只给 Host（计划第 2 节）。
/// </summary>
public class ColBalance : BasicAggregateRoot<Guid>
{
    public ColBalance(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColBalance()
    {
    }
#pragma warning restore CS8618

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>可用余额，numeric(28,18)，CHECK 非负。</summary>
    public decimal Balance { get; set; }

    /// <summary>提现冻结，numeric(28,18)，CHECK 非负且 ≤ Balance+Frozen 合计内的冻结份额。</summary>
    public decimal Frozen { get; set; }

    /// <summary>管理端并发编辑预留（计划第 5.5 节），扣款路径不读写它。</summary>
    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; }
}
