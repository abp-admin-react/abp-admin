using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Ordering.Entities;

/// <summary>
/// 订单补状态作业的自有状态（OrdJobStates，M2）：充值补插的序号游标、
/// 连续跳过计数、游标最近前进时间（7 天停滞告警用）。键是 JobType 字符串。
/// 独立小表而不是塞进 ScheduledJob.Payload——那是人可编辑的配置，不是运行期状态。
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

    /// <summary>BalanceTxnId → 连续跳过轮数（JSON 对象；成功后清除）。</summary>
    public string? SkippedTxnsJson { get; set; }

    /// <summary>游标最近一次前进的时间；超过 7 天未前进告警（计划第 3.1 节）。</summary>
    public DateTime? CursorLastAdvancedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
