using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// IScheduledJobScheduler 的录制型替身（测试用）。
/// 真实实现依赖 Quartz 的 IScheduler（测试环境没有 AbpQuartzModule），
/// 且默认的 DefaultDynamicBackgroundWorkerManager 收到 cron 会直接抛 AbpException，
/// 所以 AppService 测试用它断言「AppService 是否按预期调了调度器」。
/// </summary>
public class RecordingScheduledJobScheduler : IScheduledJobScheduler
{
    public ConcurrentQueue<(string Action, Guid JobId, string? Cron)> Calls { get; } = new();

    public Task ScheduleAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(("Schedule", job.Id, job.CronExpression));
        return Task.CompletedTask;
    }

    public Task UnscheduleAsync(Guid scheduledJobId, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(("Unschedule", scheduledJobId, null));
        return Task.CompletedTask;
    }

    public Task TriggerAsync(Guid scheduledJobId, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(("Trigger", scheduledJobId, null));
        return Task.CompletedTask;
    }

    public Task RunOnceAsync(Guid scheduledJobId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        Calls.Enqueue(("RunOnce", scheduledJobId, null));
        return Task.CompletedTask;
    }

    public Task ScheduleAllAsync(CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(("ScheduleAll", Guid.Empty, null));
        return Task.CompletedTask;
    }

    public void Clear() => Calls.Clear();

    public bool Contains(string action, Guid jobId) => Calls.Any(c => c.Action == action && c.JobId == jobId);
}
