using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Ordering.Entities;

/// <summary>
/// 按作业类型持久化订单核对、充值补插的分页位置及失败计数。ScheduledJob.Payload 只保存可编辑配置。
/// </summary>
public class OrdJobState : Entity<string>
{
#pragma warning disable CS8618
    protected OrdJobState()
    {
    }
#pragma warning restore CS8618

    public OrdJobState(string jobType)
    {
        Id = jobType;
    }

    /// <summary>充值流水补插游标：已处理到的最大 Seq（含被跳过的行）。</summary>
    public long CreditCursor { get; set; }

    /// <summary>最近七天充值重扫的独占序号上界，0 从最新一页开始。</summary>
    public long CreditRescanBeforeSeq { get; set; }

    /// <summary>七天重扫本轮翻页共用的时间锚点，读完末页后清空。</summary>
    public DateTime? CreditRescanStartedAt { get; set; }

    // 五类核对的降序分页互相独立；null 从最新主键开始下一轮。
    public Guid? PendingCollectionBeforeId { get; set; }
    public Guid? PaidCollectionBeforeId { get; set; }
    public Guid? ClosedCollectionBeforeId { get; set; }
    public Guid? PendingDebitBeforeId { get; set; }
    public Guid? ClosedDebitBeforeId { get; set; }

    /// <summary>以 BalanceTxnId 为键的失败次数。成功或发现已有订单后清除，达到告警阈值后归零。</summary>
    public string? SkippedTxnsJson { get; set; }

    /// <summary>正向游标最近前进或发出停滞告警的时间，用于限制告警频率。</summary>
    public DateTime? CursorLastAdvancedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
