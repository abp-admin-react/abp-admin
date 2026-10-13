using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 定时作业管理（T3.3 第 8 步）。与 BackgroundJobAppService 职责分离：
/// 那边管一次性队列作业（AbpBackgroundJobs 表），这边管周期性调度配置（AppScheduledJobs 表）。
/// </summary>
[Authorize(AbpAdminPermissions.ScheduledJobs.Default)]
public class ScheduledJobAppService : AbpAdminAppService, IScheduledJobAppService
{
    private readonly IRepository<ScheduledJob, Guid> _jobRepository;
    private readonly IRepository<ScheduledJobExecution, Guid> _executionRepository;
    private readonly IScheduledJobScheduler _scheduler;
    private readonly IEnumerable<IScheduledJobHandler> _handlers;
    private readonly IDistributedCache<string> _triggerCache;

    public ScheduledJobAppService(
        IRepository<ScheduledJob, Guid> jobRepository,
        IRepository<ScheduledJobExecution, Guid> executionRepository,
        IScheduledJobScheduler scheduler,
        IEnumerable<IScheduledJobHandler> handlers,
        IDistributedCache<string> triggerCache)
    {
        _jobRepository = jobRepository;
        _executionRepository = executionRepository;
        _scheduler = scheduler;
        _handlers = handlers;
        _triggerCache = triggerCache;
    }

    public virtual async Task<PagedResultDto<ScheduledJobDto>> GetListAsync(GetScheduledJobsInput input)
    {
        var queryable = await _jobRepository.GetQueryableAsync();
        var query = queryable
            .WhereIf(!input.Name.IsNullOrWhiteSpace(), x => x.Name.Contains(input.Name!))
            .WhereIf(!input.JobType.IsNullOrWhiteSpace(), x => x.JobType == input.JobType)
            .WhereIf(input.IsEnabled.HasValue, x => x.IsEnabled == input.IsEnabled);

        var count = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.OrderByDescending(x => x.CreationTime)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<ScheduledJobDto>(count, items.Select(Map).ToList());
    }

    public virtual async Task<ScheduledJobDto> GetAsync(Guid id)
    {
        var job = await _jobRepository.GetAsync(id);
        return Map(job);
    }

    [Authorize(AbpAdminPermissions.ScheduledJobs.Create)]
    public virtual async Task<ScheduledJobDto> CreateAsync(CreateScheduledJobDto input)
    {
        ValidateCronExpression(input.CronExpression);
        // GUI 测试 D10：未知 JobType 此前可入库，作业到点必败——创建时即按处理器清单白名单校验
        EnsureJobTypeKnown(input.JobType);
        // security F5：Host 专属 JobType（跨租户巡检类，见 HostOnlyScheduledJobTypes）禁止租户侧创建，
        // 否则租户管理员可自建 watchdog 作业驱动 Host 级跨租户扫描。
        EnsureJobTypeAllowedForCurrentSide(input.JobType);
        await EnsureNameUniqueAsync(input.Name);

        var job = new ScheduledJob(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Name,
            input.JobType,
            input.CronExpression,
            input.Description,
            input.IsEnabled)
        {
            Payload = input.Payload
        };

        await _jobRepository.InsertAsync(job);

        // 调度同步必须挂在 UoW 完成后：UoW 内就调 ScheduleAsync，后续 UoW 回滚
        // 会在 Quartz 里留下一个数据库没有对应记录的 job。
        if (job.IsEnabled)
        {
            ScheduleOnUowCompleted(job);
        }

        return Map(job);
    }

    [Authorize(AbpAdminPermissions.ScheduledJobs.Update)]
    public virtual async Task<ScheduledJobDto> UpdateAsync(Guid id, UpdateScheduledJobDto input)
    {
        ValidateCronExpression(input.CronExpression);

        var job = await _jobRepository.GetAsync(id);
        // security F5：UpdateDto 不含 JobType，这里挡的是租户侧已存在的 Host 专属作业行
        // （种子会为每个租户也种一份 watchdog，默认停用）被更新后重新注册进调度器。
        EnsureJobTypeAllowedForCurrentSide(job.JobType);
        if (!string.Equals(job.Name, input.Name, StringComparison.Ordinal))
        {
            await EnsureNameUniqueAsync(input.Name);
        }

        job.SetName(input.Name);
        job.SetCronExpression(input.CronExpression);
        job.Description = input.Description;
        job.Payload = input.Payload;

        await _jobRepository.UpdateAsync(job);

        if (job.IsEnabled)
        {
            // AddAsync 同名是替换语义，重注册即应用新 cron，无需重启
            ScheduleOnUowCompleted(job);
        }

        return Map(job);
    }

    [Authorize(AbpAdminPermissions.ScheduledJobs.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _jobRepository.DeleteAsync(id);

        UnscheduleOnUowCompleted(id);
    }

    [Authorize(AbpAdminPermissions.ScheduledJobs.Update)]
    [OperationLog("定时作业", "启停", BizNo = "{{id}}", Success = "将定时作业 {{scheduledJob(id)}} 的启用状态改为{{isEnabled}}")]
    public virtual async Task<ScheduledJobDto> SetEnabledAsync(Guid id, bool isEnabled)
    {
        var job = await _jobRepository.GetAsync(id);

        // security F5：启用即注册进调度器——种子给每个租户也种了 watchdog 行（默认停用），
        // 不拦这条路径的话租户管理员开个开关就能激活 Host 级跨租户巡检，等同绕过 Create 校验。
        EnsureJobTypeAllowedForCurrentSide(job.JobType);

        if (isEnabled)
        {
            job.Enable();
        }
        else
        {
            job.Disable();
        }

        await _jobRepository.UpdateAsync(job);

        if (isEnabled)
        {
            ScheduleOnUowCompleted(job);
        }
        else
        {
            UnscheduleOnUowCompleted(id);
        }

        return Map(job);
    }

    [Authorize(AbpAdminPermissions.ScheduledJobs.Trigger)]
    [OperationLog("定时作业", "手动触发", BizNo = "{{id}}", Success = "手动触发了定时作业 {{scheduledJob(id)}}")]
    public virtual async Task TriggerAsync(Guid id)
    {
        var job = await _jobRepository.GetAsync(id);
        if (!job.IsEnabled)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.ScheduledJobs.ScheduledJobDisabled);
        }

        // security F5：手动触发同样不允许租户侧驱动 Host 专属作业（防绕过启停校验直接点火）。
        EnsureJobTypeAllowedForCurrentSide(job.JobType);

        // "推送给触发者"的数据通道必须自建：动态 worker 的 JobDataMap 里只有 worker 名，
        // 闭包在 ScheduleAsync 时捕获（活到进程结束），不能把当前用户钉进去。
        // 所以走分布式缓存一次性键，RunAsync 拿到锁后读后即删。TTL 5 分钟兜底。
        // 写入必须切回 host 上下文：ABP 分布式缓存键按当前租户加 t:{id} 前缀，
        // 租户管理员触发时若带租户上下文写入，调度器（host 上下文）读的是无前缀键，
        // 永远读不到——租户侧的"完成后通知"静默失效。
        // Redis:IsEnabled=false 时退化为进程内缓存：单实例没问题，多实例下
        // Trigger 写在实例 A、Run 落在实例 B 会读不到（手动触发不推送，已知取舍）。
        if (CurrentUser.Id.HasValue)
        {
            using (CurrentTenant.Change(null))
            {
                await _triggerCache.SetAsync(
                    ScheduledJobScheduler.TriggeredByCacheKey(id),
                    CurrentUser.Id.Value.ToString(),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                    });
            }
        }

        await _scheduler.TriggerAsync(id);
    }

    public virtual async Task<PagedResultDto<ScheduledJobExecutionDto>> GetExecutionsAsync(
        Guid id, PagedAndSortedResultRequestDto input)
    {
        var queryable = await _executionRepository.GetQueryableAsync();
        var query = queryable.Where(x => x.ScheduledJobId == id);

        var count = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.OrderByDescending(x => x.StartTime)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<ScheduledJobExecutionDto>(count, items.Select(MapExecution).ToList());
    }

    public virtual Task<ListResultDto<ScheduledJobTypeDto>> GetJobTypesAsync()
    {
        // T-SJ-03：Host 专属类型对租户侧不可见（下拉不出现），与创建侧的 HostSideOnly 拒绝配套
        var items = _handlers
            .Where(h => CurrentTenant.Id == null || !HostOnlyScheduledJobTypes.All.Contains(h.JobType))
            .Select(h => new ScheduledJobTypeDto
            {
                JobType = h.JobType,
                DisplayName = L[h.DisplayNameKey]
            })
            .OrderBy(x => x.JobType)
            .ToList();

        return Task.FromResult(new ListResultDto<ScheduledJobTypeDto>(items));
    }

    public virtual Task<CronPreviewDto> PreviewCronAsync(string cronExpression)
    {
        // 渗透测试修复：整体兜底。Quartz 解析畸形输入（"bad cron !!"）抛非 ParseException，
        // 越界值（"0 0 25 * * ?"）即使解析通过也会在 GetNextValidTimeAfter 抛出——
        // 此前两条路径都是 500，前端只能显示笼统的「预览失败」。任何异常一律按非法表达式返回。
        try
        {
            if (!IsCronExpressionValid(cronExpression))
            {
                return Task.FromResult(InvalidCronPreview(cronExpression));
            }

            var expression = new Quartz.CronExpression(cronExpression);
            var next = new List<DateTime>(CronPreviewCount);
            var cursor = DateTimeOffset.UtcNow;
            for (var i = 0; i < CronPreviewCount; i++)
            {
                var fireTime = expression.GetNextValidTimeAfter(cursor);
                if (!fireTime.HasValue)
                {
                    break;
                }

                next.Add(fireTime.Value.UtcDateTime);
                cursor = fireTime.Value;
            }

            return Task.FromResult(new CronPreviewDto
            {
                IsValid = true,
                NextFireTimes = next.ToArray()
            });
        }
        catch (Exception)
        {
            return Task.FromResult(InvalidCronPreview(cronExpression));
        }
    }

    private CronPreviewDto InvalidCronPreview(string cronExpression)
    {
        // 资源串用命名占位符 {Expression}（与 BusinessException.WithData 同一套），
        // 不能走 L[key, arg] 位置参数——那会以 string.Format 处理命名占位符直接抛
        // FormatException（渗透测试回归实测）。这里本地化后手动替换。
        var message = L[AbpAdminDomainErrorCodes.ScheduledJobs.InvalidCronExpression];
        return new CronPreviewDto
        {
            IsValid = false,
            ErrorMessage = message.Value.Replace("{Expression}", cronExpression)
        };
    }

    /// <summary>PreviewCronAsync 展示"接下来 N 次触发时间"的 N（与接口注释口径一致）。</summary>
    private const int CronPreviewCount = 5;

    // 调度同步统一挂 UoW 完成后（Create/Update/SetEnabled 共用）：
    // UoW 内就调调度器，后续 UoW 回滚会在 Quartz 里留下数据库没有对应记录的 job。
    // OnCompleted 里必须 try/catch：此时事务已提交，配置写库本身成功，注册/注销失败
    // 只影响后续触发节奏；抛出去会让客户端对一次已成功的写操作收到 500（round1 审查，
    // 与 Language/LanguageText/TextTemplate AppService 的 5 处 OnCompleted 同修法）
    private void ScheduleOnUowCompleted(ScheduledJob job) =>
        UnitOfWorkManager.Current?.OnCompleted(async () =>
        {
            try
            {
                await _scheduler.ScheduleAsync(job);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e,
                    "UoW 已提交，但把定时作业 {JobId} 注册到调度器失败：作业配置已保存，下次 Update/SetEnabled 或重启时会重新注册",
                    job.Id);
            }
        });

    private void UnscheduleOnUowCompleted(Guid id) =>
        UnitOfWorkManager.Current?.OnCompleted(async () =>
        {
            try
            {
                await _scheduler.UnscheduleAsync(id);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e,
                    "UoW 已提交，但从调度器注销定时作业 {JobId} 失败：作业可能仍会被触发一次（数据库配置已删）",
                    id);
            }
        });

    /// <summary>
    /// security F5（round3）：Host 专属 JobType（见 <see cref="HostOnlyScheduledJobTypes"/>）在租户上下文一律拒绝。
    /// 复用 AbpAdmin:HostSideOnly 错误码与本地化（同 Editions 的 EnsureHostSide 语义），
    /// 区别是只对白名单 JobType 生效——普通作业租户侧照常可用。
    /// </summary>
    private void EnsureJobTypeAllowedForCurrentSide(string jobType)
    {
        if (CurrentTenant.Id != null && HostOnlyScheduledJobTypes.All.Contains(jobType))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.HostSideOnly)
                .WithData("JobType", jobType);
        }
    }

    /// <summary>
    /// GUI 测试 D10：JobType 是自由字符串，未知类型此前能入库，作业到点必败并污染执行历史。
    /// 创建/更新时按已注册处理器清单白名单校验。
    /// </summary>
    private void EnsureJobTypeKnown(string jobType)
    {
        if (!_handlers.Any(h => h.JobType == jobType))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.ScheduledJobs.UnknownJobType)
                .WithData("JobType", jobType);
        }
    }

    private async Task EnsureNameUniqueAsync(string name)
    {
        var queryable = await _jobRepository.GetQueryableAsync();
        if (await AsyncExecuter.AnyAsync(queryable.Where(x => x.Name == name)))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.ScheduledJobs.ScheduledJobNameAlreadyExists)
                .WithData("Name", name);
        }
    }

    // cron 校验必须在服务端做。Quartz 的 cron 是 7 段（秒 分 时 日 月 周 [年]），
    // 不是 Unix 的 5 段；"日"和"周"必须有且仅有一个是 ?。
    private static void ValidateCronExpression(string cronExpression)
    {
        if (!IsCronExpressionValid(cronExpression))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.ScheduledJobs.InvalidCronExpression)
                .WithData("Expression", cronExpression);
        }
    }

    /// <summary>
    /// 校验 cron 表达式是否合法。必须兜住 Quartz 抛出的一切异常：
    /// 渗透测试实测 IsValidExpression 只捕获 ParseException，而畸形输入（"bad cron !!"）
    /// 与越界值（"0 0 25 * * ?"）会抛 FormatException/ArgumentOutOfRangeException 等，
    /// 不兜住就是 500（预览）或未处理异常（创建/更新）。抛异常一律视为非法表达式。
    /// </summary>
    private static bool IsCronExpressionValid(string cronExpression)
    {
        try
        {
            return Quartz.CronExpression.IsValidExpression(cronExpression);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ScheduledJobDto Map(ScheduledJob job)
    {
        return new ScheduledJobDto
        {
            Id = job.Id,
            Name = job.Name,
            JobType = job.JobType,
            CronExpression = job.CronExpression,
            IsEnabled = job.IsEnabled,
            Description = job.Description,
            Payload = job.Payload,
            LastRunTime = job.LastRunTime,
            LastRunSuccess = job.LastRunSuccess,
            LastRunMessage = job.LastRunMessage,
            NextRunTime = job.NextRunTime
        };
    }

    private static ScheduledJobExecutionDto MapExecution(ScheduledJobExecution execution)
    {
        return new ScheduledJobExecutionDto
        {
            Id = execution.Id,
            ScheduledJobId = execution.ScheduledJobId,
            StartTime = execution.StartTime,
            EndTime = execution.EndTime,
            Success = execution.Success,
            Message = execution.Message,
            DurationMs = execution.DurationMs,
            CreationTime = execution.CreationTime
        };
    }
}
