using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 进款单（计划第 5.6 节）：唯一 (TxHash, LogIndex)。低于最低额（SkippedBelowMinDeposit）与
/// 放不下精度（PrecisionOverflow，RawAmount 留原文、Amount 空）都是终态，不归集不加余额；
/// 达标的 PendingConfirm→Confirmed 后并入归集任务；归集确认同事务标 Swept + 加余额 + 写流水
/// （入账金额只等于对应进款单的金额，充值流水的 IdempotencyKey = 进款单 id）。
/// 流水性质，不实现 ISoftDelete（计划第 2 节）。无租户列，只给 Host。
/// </summary>
public class ColDepositOrder : BasicAggregateRoot<Guid>
{
    public ColDepositOrder(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColDepositOrder()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public Guid TokenId { get; set; }

    public Guid UserId { get; set; }

    public Guid AddressId { get; set; }

    /// <summary>充值的接收地址（= ColDepositAddress.Address）。</summary>
    public string ToAddress { get; set; } = default!;

    public string FromAddress { get; set; } = default!;

    /// <summary>交易哈希（小写统一存）。</summary>
    public string TxHash { get; set; } = default!;

    /// <summary>Transfer 日志在收据内的序号。</summary>
    public int LogIndex { get; set; }

    /// <summary>链上原始整数十进制字符串（18 位小数单位）。</summary>
    public string RawAmount { get; set; } = default!;

    /// <summary>换算后的金额；PrecisionOverflow 时为 null。</summary>
    public decimal? Amount { get; set; }

    /// <summary>充值手续费（建单时按网络 DepositFeeRate 快照，向零 8 位）。历史单为 null（回退 0）。</summary>
    public decimal? Fee { get; set; }

    /// <summary>入账净额 = 毛额 − 手续费（快照）。历史单为 null/0 时入账回退毛额（线上同款兼容）。</summary>
    public decimal? ActualAmount { get; set; }

    /// <summary>建单时的费率快照（对账解释 Fee 用）。</summary>
    public decimal? FeeRateSnapshot { get; set; }

    public long BlockNumber { get; set; }

    public DateTime? BlockTime { get; set; }

    public DepositOrderStatus Status { get; set; }

    /// <summary>并入的归集任务（Confirmed→Sweeping 时回填）。</summary>
    public Guid? SweepTaskId { get; set; }

    public DateTime CreationTime { get; set; }
}
