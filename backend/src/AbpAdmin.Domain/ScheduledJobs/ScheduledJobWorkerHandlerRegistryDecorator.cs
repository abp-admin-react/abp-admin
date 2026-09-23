using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// IDynamicBackgroundWorkerHandlerRegistry 的跨节点回退装饰器。
/// 背景：AbpBackgroundWorkersQuartzModule 把「Quartz 作业」放在持久化 store（集群共享），
/// 把「handler 委托」放在进程内 registry——QuartzDynamicBackgroundWorkerAdapter.Execute 里
/// Get 未命中只打一行 "No handler registered for dynamic worker" 警告然后静默返回。
/// 开启 Quartz clustering 后触发可能落在任何节点：A 节点新建/启用作业后，B 节点重启前
/// 没有该委托，分到 B 的所有触发（含手动触发）都会被静默丢弃。
/// 本装饰器在 Get 未命中且 worker 名是 ScheduledJob:{id:N}（ScheduledJobScheduler.WorkerNamePrefix）
/// 格式时回退构造一个委托：从本次触发的 scope 解析调度器，走与常规触发完全相同的执行管线
/// （重读配置、分布式锁、租户上下文、执行记录）——RunAsync 不依赖注册时快照，任意节点可执行。
/// 其余成员全部转发给默认的进程内 registry。
/// </summary>
public class ScheduledJobWorkerHandlerRegistryDecorator : IDynamicBackgroundWorkerHandlerRegistry
{
    private readonly DynamicBackgroundWorkerHandlerRegistry _inner;

    public ScheduledJobWorkerHandlerRegistryDecorator(DynamicBackgroundWorkerHandlerRegistry inner)
    {
        _inner = inner;
    }

    public DynamicBackgroundWorkerHandler? Get(string workerName)
    {
        var handler = _inner.Get(workerName);
        if (handler != null)
        {
            return handler;
        }

        if (workerName != null &&
            workerName.StartsWith(ScheduledJobScheduler.WorkerNamePrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(
                workerName.AsSpan(ScheduledJobScheduler.WorkerNamePrefix.Length),
                "N",
                out var scheduledJobId))
        {
            return (context, cancellationToken) => context.ServiceProvider
                .GetRequiredService<IScheduledJobScheduler>()
                .RunOnceAsync(scheduledJobId, context.ServiceProvider, cancellationToken);
        }

        return null;
    }

    public void Register(string workerName, DynamicBackgroundWorkerHandler handler)
        => _inner.Register(workerName, handler);

    public bool Unregister(string workerName) => _inner.Unregister(workerName);

    public bool IsRegistered(string workerName) => _inner.IsRegistered(workerName);

    public IReadOnlyCollection<string> GetAllNames() => _inner.GetAllNames();

    public void Clear() => _inner.Clear();
}
