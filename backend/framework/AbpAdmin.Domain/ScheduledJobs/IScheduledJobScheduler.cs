using System;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 定时作业调度器（T3.3 第 7 步）。把 AppScheduledJobs 的配置行映射到
/// ABP 的 IDynamicBackgroundWorkerManager（Quartz 实现），不自研调度内核。
/// </summary>
public interface IScheduledJobScheduler
{
    /// <summary>把一条启用的 ScheduledJob 注册（或重新注册）进调度器。</summary>
    Task ScheduleAsync(ScheduledJob job, CancellationToken cancellationToken = default);

    /// <summary>取消调度。停用或删除时调用。</summary>
    Task UnscheduleAsync(Guid scheduledJobId, CancellationToken cancellationToken = default);

    /// <summary>立即触发一次，不影响原有 cron 计划。</summary>
    Task TriggerAsync(Guid scheduledJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在给定的服务容器里执行一次完整运行管线（重读配置、分布式锁、租户上下文、执行记录）。
    /// 常规触发由调度器自己调；跨节点回退路径（ScheduledJobWorkerHandlerRegistryDecorator）
    /// 在本节点进程内没有注册委托时，用 Quartz 触发 scope 里的容器调这个方法补跑。
    /// </summary>
    Task RunOnceAsync(Guid scheduledJobId, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    /// <summary>把全部启用的记录重新注册，并清掉库里已没有对应配置的孤儿 job。启动时与配置变更后调用。</summary>
    Task ScheduleAllAsync(CancellationToken cancellationToken = default);
}
