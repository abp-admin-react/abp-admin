using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 定时作业配置（T3.3，表 AppScheduledJobs）。一条记录对应 Quartz 里的一个动态 worker。
/// 系统级配置，不实现 IHasDataScope（判断依据见 04-batch3-enhancements.md T3.3 第 5 步）。
/// </summary>
public class ScheduledJob : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>唯一名称，(TenantId, Name) 唯一索引。</summary>
    public virtual string Name { get; protected set; } = default!;

    /// <summary>作业类型标识，对应某个 IScheduledJobHandler.JobType。</summary>
    public virtual string JobType { get; protected set; } = default!;

    /// <summary>Quartz cron 表达式（7 段，含秒）。</summary>
    public virtual string CronExpression { get; protected set; } = default!;

    public virtual bool IsEnabled { get; protected set; }

    public virtual string? Description { get; set; }

    /// <summary>传给 handler 的参数，JSON 字符串。为 null 时 handler 用默认值。</summary>
    public virtual string? Payload { get; set; }

    public virtual DateTime? LastRunTime { get; protected set; }

    public virtual bool? LastRunSuccess { get; protected set; }

    public virtual string? LastRunMessage { get; protected set; }

    /// <summary>由 Quartz trigger 回写，不由业务代码计算。</summary>
    public virtual DateTime? NextRunTime { get; protected set; }

    protected ScheduledJob()
    {
    }

    public ScheduledJob(
        Guid id,
        Guid? tenantId,
        string name,
        string jobType,
        string cronExpression,
        string? description = null,
        bool isEnabled = false)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ScheduledJobConsts.MaxNameLength);
        JobType = Check.NotNullOrWhiteSpace(jobType, nameof(jobType), ScheduledJobConsts.MaxJobTypeLength);
        CronExpression = Check.NotNullOrWhiteSpace(cronExpression, nameof(cronExpression), ScheduledJobConsts.MaxCronExpressionLength);
        Description = description;
        IsEnabled = isEnabled;
    }

    public virtual void SetCronExpression(string cronExpression)
    {
        CronExpression = Check.NotNullOrWhiteSpace(cronExpression, nameof(cronExpression), ScheduledJobConsts.MaxCronExpressionLength);
    }

    public virtual void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), ScheduledJobConsts.MaxNameLength);
    }

    public virtual void Enable()
    {
        IsEnabled = true;
    }

    public virtual void Disable()
    {
        IsEnabled = false;
    }

    /// <summary>回写最近一次执行结果。message 超长截断（列限长见 ScheduledJobConsts）。</summary>
    public virtual void RecordExecution(DateTime runTime, bool success, string? message)
    {
        LastRunTime = runTime;
        LastRunSuccess = success;
        LastRunMessage = message?.Truncate(ScheduledJobConsts.MaxMessageLength);
    }

    /// <summary>只写这一个派生字段。并发修改以用户的编辑为准，见 T3.3 第 7 步说明 3。</summary>
    public virtual void SetNextRunTime(DateTime? nextRunTime)
    {
        NextRunTime = nextRunTime;
    }
}
