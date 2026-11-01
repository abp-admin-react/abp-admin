using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 定时作业执行历史（表 AppScheduledJobExecutions）。
/// 系统写入的日志，没有"谁创建/谁修改"的概念，所以用 Entity&lt;Guid&gt; 而不是审计基类（只留 CreationTime）。
/// 与 ScheduledJob 一样不实现 IHasDataScope（系统级配置的历史）。
/// </summary>
public class ScheduledJobExecution : Entity<Guid>, IMultiTenant, IHasCreationTime
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid ScheduledJobId { get; protected set; }

    public virtual DateTime StartTime { get; protected set; }

    public virtual DateTime? EndTime { get; protected set; }

    public virtual bool Success { get; protected set; }

    /// <summary>限长 4000，写入时截断（EF 侧也配了 HasMaxLength，双保险）。</summary>
    public virtual string? Message { get; protected set; }

    public virtual long DurationMs { get; protected set; }

    public virtual DateTime CreationTime { get; protected set; }

    protected ScheduledJobExecution()
    {
    }

    public ScheduledJobExecution(
        Guid id,
        Guid? tenantId,
        Guid scheduledJobId,
        DateTime startTime,
        DateTime? endTime,
        bool success,
        string? message,
        long durationMs,
        DateTime creationTime)
        : base(id)
    {
        TenantId = tenantId;
        ScheduledJobId = scheduledJobId;
        StartTime = startTime;
        EndTime = endTime;
        Success = success;
        Message = message?.Truncate(ScheduledJobConsts.MaxMessageLength);
        DurationMs = durationMs;
        // CreationTime 由写入方传入（同一 IClock 基准）——清理侧 cutoff 用 _clock.Now
        // （默认时钟＝主机本地时），实体内写死 UtcNow 会在非 UTC 主机上相差一个时区，
        // 保留边界整体偏移。实体构造无 DI 上下文，
        // 由唯一生产写入方 ScheduledJobScheduler.RecordAsync（已解析 IClock）传 now 进来，
        // 与 StartTime/EndTime/LastRunTime 同基准。
        CreationTime = creationTime;
    }
}
