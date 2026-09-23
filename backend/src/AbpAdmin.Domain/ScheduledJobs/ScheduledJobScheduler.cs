using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.DataScopes;
using AbpAdmin.RealTime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// T3.3 第 7 步：把 AppScheduledJobs 配置映射到 ABP 的 IDynamicBackgroundWorkerManager。
///
/// 依赖纪律（规则出处 00-overview.md 6.5，本类的具体形状见 04 T3.3 第 7 步）：
/// 动态 worker 委托被进程级 IDynamicBackgroundWorkerHandlerRegistry 持有到进程结束，
/// 所以 RunAsync 及其调用链里除 _distributedLock / _dynamicWorkerManager 外的每个依赖
/// 都从本次触发的 serviceProvider 解析，一个都不许改成字段。
/// 两个字段例外的判据：注册生命周期不是 scoped，且整条构造依赖链不间接持有 DbContext
/// （05-reference-facts 14.1/14.2、13.6/13.7 已核实）。
/// </summary>
public class ScheduledJobScheduler : DomainService, IScheduledJobScheduler
{
    private readonly IDynamicBackgroundWorkerManager _dynamicWorkerManager;
    private readonly IAbpDistributedLock _distributedLock;

    public ScheduledJobScheduler(
        IDynamicBackgroundWorkerManager dynamicWorkerManager,
        IAbpDistributedLock distributedLock)
    {
        _dynamicWorkerManager = dynamicWorkerManager;
        _distributedLock = distributedLock;
    }

    public virtual async Task ScheduleAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        if (_dynamicWorkerManager is not ISupportsCronScheduling)
        {
            throw new AbpException(
                $"当前的 {_dynamicWorkerManager.GetType().Name} 不支持 cron 调度。" +
                $"请确认 AbpBackgroundWorkersQuartzModule 已加入 DependsOn。");
        }

        // 只把 Id 捕进闭包，不要捕 job 实体本身。这个委托会被存进进程级的
        // IDynamicBackgroundWorkerHandlerRegistry，活到进程结束；捕住实体等于把
        // "排程那一刻的配置快照"永久钉住。RunAsync 第 1 步重新查库正是为了避免这个。
        var jobId = job.Id;

        // AddAsync 没有 replace 参数：同名注册是无条件替换（05 14.2，上游 XML 文档明文），
        // 启动时每个实例各跑一遍全量重注册是安全的。
        await _dynamicWorkerManager.AddAsync(
            WorkerName(jobId),
            new DynamicBackgroundWorkerSchedule { CronExpression = job.CronExpression },
            // 委托第一个参数是 DynamicBackgroundWorkerExecutionContext，不是 IServiceProvider；
            // context.ServiceProvider 才是"本次触发这个 scope"的容器。
            async (context, ct) => await RunAsync(jobId, context.ServiceProvider, ct),
            cancellationToken);
    }

    public virtual async Task UnscheduleAsync(Guid scheduledJobId, CancellationToken cancellationToken = default)
    {
        await _dynamicWorkerManager.RemoveAsync(WorkerName(scheduledJobId), cancellationToken);
    }

    public virtual async Task TriggerAsync(Guid scheduledJobId, CancellationToken cancellationToken = default)
    {
        // 走 Quartz 触发而不是直接调 handler：复用 RunAsync 的全部保护逻辑
        // （分布式锁、租户上下文、执行记录），且不阻塞当前请求。
        // 这里从 LazyServiceProvider 解析 IScheduler——本方法跑在调用方（AppService）的
        // 请求 scope 里，不是动态 worker 委托路径，scheduler 本身是单例（05 14.3）。
        var scheduler = LazyServiceProvider.GetRequiredService<IScheduler>();
        await scheduler.TriggerJob(new JobKey(QuartzKeyName(scheduledJobId)), cancellationToken);
    }

    public virtual async Task ScheduleAllAsync(CancellationToken cancellationToken = default)
    {
        // 本方法在启动钩子 / AppService 请求里跑，不是动态 worker 委托路径，
        // 依赖从 LazyServiceProvider（当前 scope）解析即可。
        var unitOfWorkManager = LazyServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var dataFilter = LazyServiceProvider.GetRequiredService<IDataFilter>();
        var repository = LazyServiceProvider.GetRequiredService<IRepository<ScheduledJob, Guid>>();

        List<ScheduledJob> jobs;
        // 必须关多租户过滤器拿到全部租户的作业：此刻 CurrentTenant 是 host，
        // 租户级记录不过滤掉就注册不上。
        using (dataFilter.Disable<IMultiTenant>())
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            jobs = await repository.GetListAsync(cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        foreach (var job in jobs.Where(x => x.IsEnabled))
        {
            await ScheduleAsync(job, cancellationToken);
        }

        // 清孤儿：Quartz 里有 DynamicWorker:ScheduledJob: 前缀的 job，
        // 但 AppScheduledJobs 里已没有对应记录（比如直接删库删了配置行）。
        var existingIds = jobs.Select(x => x.Id).ToHashSet();
        var scheduler = LazyServiceProvider.GetRequiredService<IScheduler>();
        var jobKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup), cancellationToken);

        foreach (var jobKey in jobKeys)
        {
            if (!jobKey.Name.StartsWith(QuartzKeyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (!Guid.TryParse(jobKey.Name[QuartzKeyPrefix.Length..], out var orphanId) ||
                existingIds.Contains(orphanId))
            {
                continue;
            }

            // Logger 在这里可用：ScheduleAllAsync 跑在调用方（启动钩子 / AppService 请求）
            // 的存活 scope 里，LazyServiceProvider 指向的正是这个 scope。
            // 不要把这个写法带进 RunAsync——那里的本实例早已活过它的构造 scope。
            Logger.LogInformation(
                "Removing orphaned Quartz job {JobKey}: no matching AppScheduledJobs row.", jobKey);
            await _dynamicWorkerManager.RemoveAsync(WorkerName(orphanId), cancellationToken);
        }
    }

    /// <summary>动态 worker 名前缀。Quartz 集群下跨节点回退注册表按此识别本模块的 worker（见 ScheduledJobWorkerHandlerRegistryDecorator）。</summary>
    public const string WorkerNamePrefix = "ScheduledJob:";

    private static string WorkerName(Guid scheduledJobId) => $"{WorkerNamePrefix}{scheduledJobId:N}";

    // trigger key / job key 的格式已核实（05 14.6）：上游用单参构造 new TriggerKey($"DynamicWorker:{workerName}")，
    // 前缀在 name 段，group 走 Quartz 默认组 DEFAULT。常量拼接保证格式只有一份 owner。
    private const string QuartzKeyPrefix = "DynamicWorker:" + WorkerNamePrefix;

    private static string QuartzKeyName(Guid scheduledJobId) => $"DynamicWorker:{WorkerName(scheduledJobId)}";

    /// <summary>
    /// 供跨节点回退路径调用（ScheduledJobWorkerHandlerRegistryDecorator）：本节点进程内 registry
    /// 没有委托、但 Quartz 集群把触发分到了本节点时，由回退委托走与常规触发完全相同的执行管线
    /// （重读配置、分布式锁、租户上下文、执行记录）。RunAsync 每次触发都重读数据库，
    /// 不依赖注册时的任何快照，因此任意节点都能安全执行任意作业。
    /// </summary>
    public Task RunOnceAsync(Guid scheduledJobId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => RunAsync(scheduledJobId, serviceProvider, cancellationToken);

    private async Task RunAsync(Guid scheduledJobId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        // 0. 本次触发要用的 scoped 服务，全部从传进来的 serviceProvider 解析。
        //    一个都不许改成字段（理由见类注释与 04 T3.3 第 7 步那张依赖表）。
        //    配置读取 / 触发缓存 / handler 执行三段已分别提取到
        //    LoadJobConfigAsync / TryConsumeTriggeredByAsync / ExecuteHandlerAsync，
        //    依赖同样从各自的 serviceProvider 参数解析。
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();
        var handlers = serviceProvider.GetServices<IScheduledJobHandler>();

        // Logger 属性也不能用：它经 LazyServiceProvider 从【构造本实例的那个 scope】
        // （启动排程时的 scope，早被释放）惰性解析，触发时已 disposed——e2e 实测堆栈顶在
        // DomainService.get_Logger。日志与别的依赖同规，从本次触发的 serviceProvider 解析。
        var logger = serviceProvider.GetRequiredService<ILogger<ScheduledJobScheduler>>();

        // 低成本自检：连续两次触发这个值应当不同（每次触发都是新 scope，05 14.3 已核实）。
        logger.LogDebug("Scheduled job {JobId} executing in scope {ScopeId}.",
            scheduledJobId, serviceProvider.GetHashCode());

        // 1. 重新读一次配置（cron 可能已改，Payload 可能已改，也可能已被停用）。
        var job = await LoadJobConfigAsync(serviceProvider, scheduledJobId, cancellationToken);

        if (job is null || !job.IsEnabled)
        {
            await UnscheduleAsync(scheduledJobId, cancellationToken);
            return;
        }

        // 2. QuartzDynamicBackgroundWorkerAdapter 没有 [DisallowConcurrentExecution]，
        //    长任务会与下一次触发重叠，必须自己加分布式锁（执行区间互斥；
        //    触发层互斥由 Quartz clustering 负责，两者分工不替代）。
        //    等待时间 TimeSpan.Zero：拿不到说明上一次还在跑，直接跳过，不排队积压。
        await using var handle = await _distributedLock.TryAcquireAsync(
            WorkerName(scheduledJobId), TimeSpan.Zero, cancellationToken);

        // 手动触发者的缓存键必须在拿到锁之后再消费：放在拿锁前，锁被占（上一次还在跑）
        // 跳过时键已被删、触发者永远收不到回音；留着不管又会在下一次 cron 触发时被误
        // 消费成「已完成」。跳过分支同样消费并如实推送「已跳过」。
        if (handle is null)
        {
            logger.LogInformation(
                "Scheduled job {JobId} is still running, skipping this trigger.", scheduledJobId);
            await NotifyTriggererAsync(serviceProvider,
                await TryConsumeTriggeredByAsync(serviceProvider, scheduledJobId, cancellationToken),
                scheduledJobId, job.Name, success: false,
                message: "上一次执行仍在进行，本次手动触发已跳过",
                cancellationToken);
            return;
        }

        // 1.5.（移到拿锁之后）一次性消费"手动触发者"缓存键（第 8/9 步的推送通道）。
        //      读到说明这次是某人点的"立即触发"，完成后推送给他；读不到就是 cron 自动触发。
        var triggeredByUserId = await TryConsumeTriggeredByAsync(serviceProvider, scheduledJobId, cancellationToken);

        // 3. 切租户上下文。Change 必须包住后面每一次 Begin：UoW 建 DbContext 时
        //    才去解析连接串与多租户过滤器，顺序反了就落到错误的租户上。
        using (currentTenant.Change(job.TenantId))
        {
            var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType);
            if (handler is null)
            {
                var notFoundMessage = $"未找到 JobType 为 {job.JobType} 的 handler";
                await RecordAsync(serviceProvider, job, success: false, notFoundMessage);
                await NotifyTriggererAsync(serviceProvider, triggeredByUserId,
                    job.Id, job.Name, success: false, message: notFoundMessage, cancellationToken);
                return;
            }

            var (success, message, elapsedMs) =
                await ExecuteHandlerAsync(serviceProvider, job, handler, cancellationToken);

            await RecordAsync(serviceProvider, job, success, message, elapsedMs);

            // 执行结束后回写 NextRunTime（展示用派生值，从 Quartz 取而不是自己算 cron）。
            await WriteBackNextRunTimeAsync(serviceProvider, job, cancellationToken);

            // 第 9 步：手动触发的完成推送。cron 自动触发（triggeredByUserId 为 null）不推送。
            await NotifyTriggererAsync(serviceProvider, triggeredByUserId,
                job.Id, job.Name, success, message, cancellationToken);
        }
    }

    /// <summary>
    /// 把手动触发的执行结果推送给触发者。cron 自动触发（triggeredByUserId 为 null）不推送。
    /// static 有意：依赖从参数 serviceProvider 解析（与 LoadJobConfigAsync 同一纪律）。
    /// </summary>
    private static async Task NotifyTriggererAsync(
        IServiceProvider serviceProvider,
        Guid? triggeredByUserId,
        Guid scheduledJobId,
        string jobName,
        bool success,
        string? message,
        CancellationToken cancellationToken)
    {
        if (!triggeredByUserId.HasValue)
        {
            return;
        }

        var realTimeNotifier = serviceProvider.GetRequiredService<IRealTimeNotifier>();
        await realTimeNotifier.NotifyUserAsync(triggeredByUserId.Value, new RealTimeMessage
        {
            Name = RealTimeMessageNames.JobCompleted,
            Payload = new
            {
                scheduledJobId,
                jobName,
                success,
                message
            },
        }, cancellationToken);
    }

    /// <summary>
    /// RunAsync 第 1 步：重新读一次作业配置（cron 可能已改，Payload 可能已改，也可能已被停用）。
    /// static 有意：依赖全部从参数 serviceProvider 解析，编译器拒绝偷用实例字段
    /// （与 RecordAsync 同一纪律）。返回 null 表示作业已被删/停用，调用方走注销分支。
    /// </summary>
    private static async Task<ScheduledJob?> LoadJobConfigAsync(
        IServiceProvider serviceProvider, Guid scheduledJobId, CancellationToken cancellationToken)
    {
        var repository = serviceProvider.GetRequiredService<IRepository<ScheduledJob, Guid>>();
        var unitOfWorkManager = serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        var dataFilter = serviceProvider.GetRequiredService<IDataFilter>();

        // 后台触发路径上没有环境 UoW，仓储调用必须自己开。
        // 多租户过滤器必须显式关掉：此刻还没切租户，CurrentTenant 是 host，
        // 租户自己的作业会被过滤掉、FindAsync 返回 null，于是走到 UnscheduleAsync
        // ——把租户的作业全注销掉。using 只包住这一次读取。
        ScheduledJob? job;
        using (dataFilter.Disable<IMultiTenant>())
        using (var readUow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            job = await repository.FindAsync(scheduledJobId, cancellationToken: cancellationToken);
            await readUow.CompleteAsync(cancellationToken);
        }

        return job;
    }

    /// <summary>
    /// RunAsync 第 1.5 步：一次性消费"手动触发者"缓存键。返回触发者 UserId，
    /// null 表示这次是 cron 自动触发（完成后不推送）。
    /// </summary>
    private static async Task<Guid?> TryConsumeTriggeredByAsync(
        IServiceProvider serviceProvider, Guid scheduledJobId, CancellationToken cancellationToken)
    {
        var triggerCache = serviceProvider.GetRequiredService<IDistributedCache<string>>();
        var triggerCacheKey = TriggeredByCacheKey(scheduledJobId);
        var triggeredByRaw = await triggerCache.GetAsync(triggerCacheKey, token: cancellationToken);
        if (triggeredByRaw is not null)
        {
            await triggerCache.RemoveAsync(triggerCacheKey, token: cancellationToken);
        }

        return Guid.TryParse(triggeredByRaw, out var triggeredByGuid) ? triggeredByGuid : null;
    }

    /// <summary>
    /// RunAsync 第 3 步：在独立事务 UoW 里执行 handler（失败即回滚 handler 的写入），
    /// 并把成败与耗时返回给调用方落执行记录。
    /// </summary>
    private static async Task<(bool Success, string? Message, long ElapsedMs)> ExecuteHandlerAsync(
        IServiceProvider serviceProvider, ScheduledJob job, IScheduledJobHandler handler,
        CancellationToken cancellationToken)
    {
        var unitOfWorkManager = serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        var dataFilter = serviceProvider.GetRequiredService<IDataFilter>();
        var logger = serviceProvider.GetRequiredService<ILogger<ScheduledJobScheduler>>();

        var stopwatch = Stopwatch.StartNew();
        bool success;
        string? message;
        try
        {
            // handler 是普通 ITransientDependency，不被 UoW 拦截器包着，这层 UoW 由我们开。
            // 事务在这里，失败即回滚 handler 的写入。
            using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                // 后台没有登录用户，行级数据范围算不出来（T1.5 约定 fail-closed 零行可见），
                // 必须显式关掉数据范围过滤器。范围只包住 handler 这一次调用：
                // RecordAsync 写的 ScheduledJobExecution 不实现 IHasDataScope，不跟着放开。
                using (dataFilter.Disable<IDataScopeEnabled>())
                {
                    await handler.ExecuteAsync(new ScheduledJobContext
                    {
                        ScheduledJobId = job.Id,
                        TenantId = job.TenantId,
                        JobType = job.JobType,
                        Payload = job.Payload,
                        CancellationToken = cancellationToken,
                    });
                }

                await uow.CompleteAsync(cancellationToken);
            }

            success = true;
            message = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 取消（停机/令牌中止）不是执行失败：落失败执行记录会把 LastRunSuccess 打成 false、
            // 污染执行历史，误导排障。这里单独识别并重抛——动态 worker 适配器只记一行日志不落库，
            // RunAsync 后续的 RecordAsync/NextRunTime 回写/完成推送随之跳过（下次触发自会补上）。
            // 过滤条件限定"确因本作业令牌取消"：其他来源的 OCE 仍按真失败走下面的通用分支。
            logger.LogWarning(
                "Scheduled job {JobId} was cancelled before completion; no failure record will be written.",
                job.Id);
            throw;
        }
        catch (Exception ex)
        {
            // 必须自己 catch 并落库：适配器会把异常吞掉只留一行日志（05 14.3 已核实），
            // 不落库前端执行历史就是一片空白。
            logger.LogException(ex);
            success = false;
            message = ex.Message.Truncate(ScheduledJobConsts.MaxMessageLength);
        }

        return (success, message, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// 回写 NextRunTime。必须自己开一个新 UoW（Quartz 触发路径上没有环境 UoW），
    /// 并且在新 UoW 里把实体重新查一次——传入的 job 是在本方法第 1 步那个已提交并释放的
    /// UoW 里读出来的，新 UoW 的 DbContext 根本不跟踪它，直接 UpdateAsync 行为取决于仓储实现。
    /// </summary>
    private async Task WriteBackNextRunTimeAsync(
        IServiceProvider serviceProvider, ScheduledJob job, CancellationToken cancellationToken)
    {
        // scheduler 从 serviceProvider 解析：这段跑在 RunAsync 的调用链上，守类注释那条规则。
        var scheduler = serviceProvider.GetRequiredService<IScheduler>();
        var trigger = await scheduler.GetTrigger(new TriggerKey(QuartzKeyName(job.Id)), cancellationToken);
        var nextRunTime = trigger?.GetNextFireTimeUtc()?.UtcDateTime;

        var jobRepository = serviceProvider.GetRequiredService<IRepository<ScheduledJob, Guid>>();
        var unitOfWorkManager = serviceProvider.GetRequiredService<IUnitOfWorkManager>();

        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var fresh = await jobRepository.FindAsync(job.Id, cancellationToken: cancellationToken);
            if (fresh is null)
            {
                // 作业在本次执行期间被删了。执行已完成、记录已落库，只是没有"下次"可写，
                // 当成正常路径而不是异常路径。
                serviceProvider.GetRequiredService<ILogger<ScheduledJobScheduler>>().LogDebug(
                    "Scheduled job {JobId} no longer exists, skipping NextRunTime write-back.", job.Id);
                return;
            }

            // 只动 NextRunTime 这一个派生字段：执行期间用户可能改过 cron/启停/payload，
            // 那些改动一律以对方为准，不能把旧快照整体拷回来。
            fresh.SetNextRunTime(nextRunTime);
            await jobRepository.UpdateAsync(fresh, cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }
    }

    // RecordAsync 是 static 有意的：它一个实例成员都不许用（仓储与 UoW 都从参数里那个
    // serviceProvider 解析），static 把这件事在签名上钉死，编译器会拒绝任何偷用字段的写法。
    // 日志也从参数解析，不要为它改回实例方法。
    private static async Task RecordAsync(
        IServiceProvider serviceProvider, ScheduledJob job, bool success,
        string? message = null, long durationMs = 0)
    {
        var executionRepository = serviceProvider
            .GetRequiredService<IRepository<ScheduledJobExecution, Guid>>();
        var jobRepository = serviceProvider.GetRequiredService<IRepository<ScheduledJob, Guid>>();
        var unitOfWorkManager = serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        var logger = serviceProvider.GetRequiredService<ILogger<ScheduledJobScheduler>>();
        var guidGenerator = serviceProvider.GetRequiredService<IGuidGenerator>();
        var clock = serviceProvider.GetRequiredService<IClock>();

        // 独立 UoW，不能与 handler 那层共用（00-overview 6.5《失败也要留痕》）：
        // 共用时 handler 一失败，这条失败记录跟着一起回滚，历史表里什么都没有。
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var now = clock.Now;

            await executionRepository.InsertAsync(new ScheduledJobExecution(
                guidGenerator.Create(),
                job.TenantId,
                job.Id,
                startTime: now.AddMilliseconds(-durationMs),
                endTime: now,
                success,
                message,
                durationMs,
                // now 与 StartTime/EndTime 同一 IClock 基准：清理侧 cutoff 也走 _clock.Now，
                // 非 UTC 主机上保留边界不再因 CreationTime(UTC) vs cutoff(本地时) 偏移一个时区差
                creationTime: now));

            // 同步作业的 LastRun* 展示字段。重新查一次：传入的 job 来自已释放的读 UoW，
            // 本 UoW 的 DbContext 不跟踪它。
            var fresh = await jobRepository.FindAsync(job.Id);
            if (fresh is not null)
            {
                fresh.RecordExecution(now, success, message);
                await jobRepository.UpdateAsync(fresh);
            }
            else
            {
                logger.LogDebug(
                    "Scheduled job {JobId} no longer exists, execution recorded without LastRun update.",
                    job.Id);
            }

            await uow.CompleteAsync();
        }
    }

    /// <summary>手动触发者的缓存键（AppService 写、RunAsync 读后即删，一次性消费）。
    /// 契约两侧都必须在 host（无租户）上下文下读写：ABP 分布式缓存键按当前租户加
    /// t:{id} 前缀，AppService 侧写入已用 CurrentTenant.Change(null) 对齐；
    /// 若把消费点挪进下方租户作用域、或改在租户上下文写，完成推送会对租户管理员静默失效。</summary>
    public static string TriggeredByCacheKey(Guid scheduledJobId) => $"ScheduledJob:Trigger:{scheduledJobId:N}";
}
