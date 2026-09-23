using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.ScheduledJobs;

/* T3.3 定时作业集成测试。
 * 覆盖验收标准里的服务端条目：
 * - 提交 "0 3 * * *"（Unix 5 段）与 "0 0 3 * * *"（日和周都是 *）返回 InvalidCronExpression
 * - 提交 "0 0 3 * * ?" 通过，PreviewCronAsync 返回接下来 5 天的凌晨 3 点
 * - 增删改/启停后调度器被同步调用（录制型替身断言）
 * - 停用作业不能手动触发
 * - 租户隔离：租户上下文看不到 host 的作业
 */
public abstract class ScheduledJobAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IScheduledJobAppService _appService;
    private readonly IRepository<ScheduledJob, Guid> _jobRepository;
    private readonly IRepository<ScheduledJobExecution, Guid> _executionRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedCache<string> _triggerCache;
    private readonly ICurrentUser _currentUser;
    private readonly RecordingScheduledJobScheduler _scheduler;

    protected ScheduledJobAppServiceTests()
    {
        _appService = GetRequiredService<IScheduledJobAppService>();
        _jobRepository = GetRequiredService<IRepository<ScheduledJob, Guid>>();
        _executionRepository = GetRequiredService<IRepository<ScheduledJobExecution, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _triggerCache = GetRequiredService<IDistributedCache<string>>();
        _currentUser = GetRequiredService<ICurrentUser>();
        _scheduler = (RecordingScheduledJobScheduler)GetRequiredService<IScheduledJobScheduler>();
    }

    private async Task CleanupAsync()
    {
        _scheduler.Clear();
        await WithUnitOfWorkAsync(async () =>
        {
            await _executionRepository.DeleteAsync(_ => true);
            await _jobRepository.DeleteAsync(_ => true);
        });
    }

    private static CreateScheduledJobDto CreateInput(string name, string cron = "0 0 3 * * ?")
    {
        return new CreateScheduledJobDto
        {
            Name = name,
            JobType = "AbpAdmin.ScheduledJobExecutionCleanup",
            CronExpression = cron,
            IsEnabled = false,
            Description = "测试作业"
        };
    }

    [Fact]
    public async Task CreateAsync_Should_Create_Job()
    {
        await CleanupAsync();

        var result = await _appService.CreateAsync(CreateInput("test-job"));

        result.Id.ShouldNotBe(Guid.Empty);
        result.Name.ShouldBe("test-job");
        result.IsEnabled.ShouldBeFalse();

        var job = await _jobRepository.GetAsync(result.Id);
        job.CronExpression.ShouldBe("0 0 3 * * ?");

        // 默认停用，不应注册进调度器
        _scheduler.Contains("Schedule", result.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Unix5Field_Cron()
    {
        await CleanupAsync();

        // Unix 5 段：Quartz 报无效
        var exception = await Should.ThrowAsync<BusinessException>(
            _appService.CreateAsync(CreateInput("bad-cron-job", "0 3 * * *")));

        exception.Code.ShouldBe("AbpAdmin:InvalidCronExpression");
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_DayOfWeek_Star_Cron()
    {
        await CleanupAsync();

        // 日和周都用 *：Quartz 不允许（必须有且仅有一个是 ?）
        var exception = await Should.ThrowAsync<BusinessException>(
            _appService.CreateAsync(CreateInput("bad-cron-job-2", "0 0 3 * * *")));

        exception.Code.ShouldBe("AbpAdmin:InvalidCronExpression");
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Duplicate_Name()
    {
        await CleanupAsync();

        await _appService.CreateAsync(CreateInput("dup-job"));

        var exception = await Should.ThrowAsync<BusinessException>(
            _appService.CreateAsync(CreateInput("dup-job")));

        exception.Code.ShouldBe("AbpAdmin:ScheduledJobNameAlreadyExists");
    }

    [Fact]
    public async Task UpdateAsync_Should_Update_Cron_And_Reject_Invalid_Cron()
    {
        await CleanupAsync();

        var created = await _appService.CreateAsync(CreateInput("update-job"));

        var updated = await _appService.UpdateAsync(created.Id, new UpdateScheduledJobDto
        {
            Name = "update-job",
            CronExpression = "0 */30 * * * ?",
            Description = "改 cron"
        });

        updated.CronExpression.ShouldBe("0 */30 * * * ?");

        await Should.ThrowAsync<BusinessException>(
            _appService.UpdateAsync(created.Id, new UpdateScheduledJobDto
            {
                Name = "update-job",
                CronExpression = "0 3 * * *"
            }));
    }

    [Fact]
    public async Task SetEnabledAsync_Should_Schedule_And_Unschedule()
    {
        await CleanupAsync();

        var created = await _appService.CreateAsync(CreateInput("toggle-job"));

        var enabled = await _appService.SetEnabledAsync(created.Id, true);
        enabled.IsEnabled.ShouldBeTrue();
        _scheduler.Contains("Schedule", created.Id).ShouldBeTrue();

        var disabled = await _appService.SetEnabledAsync(created.Id, false);
        disabled.IsEnabled.ShouldBeFalse();
        _scheduler.Contains("Unschedule", created.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteAsync_Should_Unschedule()
    {
        await CleanupAsync();

        var created = await _appService.CreateAsync(CreateInput("delete-job"));
        await _appService.SetEnabledAsync(created.Id, true);

        await _appService.DeleteAsync(created.Id);

        _scheduler.Contains("Unschedule", created.Id).ShouldBeTrue();
        (await _jobRepository.FindAsync(created.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task TriggerAsync_Should_Reject_Disabled_Job()
    {
        await CleanupAsync();

        var created = await _appService.CreateAsync(CreateInput("trigger-disabled-job"));

        var exception = await Should.ThrowAsync<BusinessException>(
            _appService.TriggerAsync(created.Id));

        exception.Code.ShouldBe("AbpAdmin:ScheduledJobDisabled");
    }

    [Fact]
    public async Task TriggerAsync_Should_Call_Scheduler_For_Enabled_Job()
    {
        await CleanupAsync();

        var created = await _appService.CreateAsync(CreateInput("trigger-job"));
        await _appService.SetEnabledAsync(created.Id, true);

        await _appService.TriggerAsync(created.Id);

        _scheduler.Contains("Trigger", created.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task TriggerAsync_Should_Write_Trigger_Key_In_Host_Context()
    {
        // 回归：分布式缓存键按当前租户加 t:{id} 前缀。租户管理员触发时若带租户上下文写入，
        // 调度器（host 上下文）读的是无前缀键、永远读不到——完成推送对租户侧静默失效。
        // 契约：写入必须在 CurrentTenant.Change(null) 下，使 host 侧读取命中同一把键。
        await CleanupAsync();

        var tenantId = Guid.NewGuid();
        Guid jobId;
        using (_currentTenant.Change(tenantId))
        {
            var created = await _appService.CreateAsync(CreateInput("tenant-trigger-job"));
            await _appService.SetEnabledAsync(created.Id, true);
            jobId = created.Id;
        }

        var adminId = _currentUser.GetId();
        using (_currentTenant.Change(tenantId))
        {
            await _appService.TriggerAsync(jobId);
        }

        string? stored;
        using (_currentTenant.Change(null))
        {
            stored = await _triggerCache.GetAsync(ScheduledJobScheduler.TriggeredByCacheKey(jobId));
        }

        stored.ShouldBe(adminId.ToString());
    }

    [Fact]
    public async Task PreviewCronAsync_Should_Return_Next_5_Days_At_3Am()
    {
        var result = await _appService.PreviewCronAsync("0 0 3 * * ?");

        result.IsValid.ShouldBeTrue();
        result.NextFireTimes.Length.ShouldBe(5);

        foreach (var fireTime in result.NextFireTimes)
        {
            // cron 的默认时区是服务器本地时区（已实测 Quartz 3.15.0），3 点指本地凌晨 3 点
            var local = fireTime.ToLocalTime();
            local.Hour.ShouldBe(3);
            local.Minute.ShouldBe(0);
            local.Second.ShouldBe(0);
        }

        // 相邻两次应恰好间隔 1 天
        for (var i = 1; i < result.NextFireTimes.Length; i++)
        {
            (result.NextFireTimes[i] - result.NextFireTimes[i - 1]).ShouldBe(TimeSpan.FromDays(1));
        }
    }

    [Fact]
    public async Task PreviewCronAsync_Should_Return_Invalid_For_Bad_Expression()
    {
        var result = await _appService.PreviewCronAsync("0 3 * * *");

        result.IsValid.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        // 错误消息是本地化文案，不是错误码本身
        result.ErrorMessage.ShouldNotBe("AbpAdmin:InvalidCronExpression");
    }

    [Fact]
    public async Task PreviewCronAsync_Should_Return_Invalid_Not_500_For_Malformed_Expression()
    {
        // 渗透测试实测：Quartz 对 "bad cron !!" 抛的不是 ParseException（500 路径），
        // 预览必须整体兜底、按非法表达式返回而不是炸 500
        var result = await _appService.PreviewCronAsync("bad cron !!");

        result.IsValid.ShouldBeFalse();
        result.NextFireTimes.ShouldBeEmpty();
        result.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
        // {Expression} 占位符由参数填充，不能渲染成字面量
        result.ErrorMessage.ShouldNotContain("{Expression}");
    }

    [Fact]
    public async Task PreviewCronAsync_Should_Return_Invalid_Not_500_For_Out_Of_Range_Value()
    {
        // 渗透测试实测："0 0 25 * * ?"（小时 25）解析或推算阶段抛异常（500 路径），
        // 必须按非法表达式返回
        var result = await _appService.PreviewCronAsync("0 0 25 * * ?");

        result.IsValid.ShouldBeFalse();
        result.NextFireTimes.ShouldBeEmpty();
        result.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Out_Of_Range_Cron_As_Business_Error()
    {
        // 与预览同一根因：越界 cron 在创建链路上必须是 400 业务异常，不能 500
        var exception = await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _appService.CreateAsync(CreateInput("越界值回归", "0 0 25 * * ?"));
        });

        exception.Code.ShouldBe("AbpAdmin:InvalidCronExpression");
    }

    [Fact]
    public async Task PreviewCronAsync_Should_Return_Chinese_Error_Under_ZhHans()
    {
        // 验收：提交非法 cron 时 error.message 是中文（提示 Quartz 需要 7 段）不是 code
        using (CultureHelper.Use("zh-Hans"))
        {
            var result = await _appService.PreviewCronAsync("0 3 * * *");

            result.IsValid.ShouldBeFalse();
            result.ErrorMessage.ShouldNotBeNull();
            result.ErrorMessage.ShouldContain("Quartz 需要 7 段");
        }
    }

    [Fact]
    public async Task GetJobTypesAsync_Should_Return_BuiltIn_Handlers()
    {
        var result = await _appService.GetJobTypesAsync();

        var jobTypes = result.Items.Select(x => x.JobType).ToList();
        jobTypes.ShouldContain("AbpAdmin.AuditLogCleanup");
        jobTypes.ShouldContain("AbpAdmin.AuditLogExportCleanup");
        jobTypes.ShouldContain("AbpAdmin.LanguageSync");
        jobTypes.ShouldContain("AbpAdmin.GdprRequestCleanup");
        jobTypes.ShouldContain("AbpAdmin.ScheduledJobExecutionCleanup");
        jobTypes.ShouldContain("AbpAdmin.ThumbnailBackfill");
        // 广播断链看门狗（handler 在 Application 层）：作业类型下拉必须能看到它才能启用
        jobTypes.ShouldContain("AbpAdmin.BroadcastNotificationWatchdog");
        result.Items.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.DisplayName));
    }

    [Fact]
    public async Task GetExecutionsAsync_Should_Return_Executions_Of_Job()
    {
        await CleanupAsync();

        var created = await _appService.CreateAsync(CreateInput("history-job"));

        await WithUnitOfWorkAsync(async () =>
        {
            await _executionRepository.InsertAsync(new ScheduledJobExecution(
                Guid.NewGuid(), null, created.Id,
                DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow,
                success: true, message: null, durationMs: 1000,
                creationTime: DateTime.UtcNow));
        });

        var result = await _appService.GetExecutionsAsync(created.Id,
            new PagedAndSortedResultRequestDto { MaxResultCount = 10 });

        result.TotalCount.ShouldBe(1);
        result.Items[0].ScheduledJobId.ShouldBe(created.Id);
        result.Items[0].Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GetListAsync_Should_Isolate_By_Tenant()
    {
        await CleanupAsync();

        var hostJob = await _appService.CreateAsync(CreateInput("host-job"));
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            var tenantList = await _appService.GetListAsync(new GetScheduledJobsInput
            {
                MaxResultCount = 10
            });
            tenantList.TotalCount.ShouldBe(0);

            await _appService.CreateAsync(CreateInput("tenant-job"));
        }

        var hostList = await _appService.GetListAsync(new GetScheduledJobsInput
        {
            MaxResultCount = 10
        });
        hostList.TotalCount.ShouldBe(1);
        hostList.Items[0].Id.ShouldBe(hostJob.Id);
    }

    [Fact]
    public async Task Tenant_Should_Be_Rejected_On_All_Four_Entry_Points_For_Host_Only_Job_Type()
    {
        // round3 security F5 的回归锚（round4 前零锚定）：Host 专属 JobType（广播看门狗，
        // 会关租户过滤器跨租户巡检）在租户上下文的 Create/Update/SetEnabled/Trigger 一律拒绝；
        // 普通 JobType 不受影响（上面其余用例已覆盖普通类型的租户侧可用性）。
        await CleanupAsync();
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            // 1) Create：租户自建 watchdog 作业 → 拒绝
            var create = await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(
                new CreateScheduledJobDto
                {
                    Name = "tenant-watchdog",
                    JobType = ScheduledJobConsts.BroadcastNotificationWatchdogJobType,
                    CronExpression = "0 */15 * * * ?",
                    IsEnabled = false,
                    Description = "租户侧自建看门狗"
                }));
            create.Code.ShouldBe(AbpAdminDomainErrorCodes.HostSideOnly);

            // 2/3/4) Update/SetEnabled/Trigger：对绕过 AppService 落在租户的既有 watchdog 行同样拒绝
            //（防白名单或 JobType 漂移后存量行失守）
            var seededWatchdogId = Guid.Empty;
            await WithUnitOfWorkAsync(async () =>
            {
                var job = new ScheduledJob(
                    Guid.NewGuid(), tenantId, "seeded-watchdog",
                    ScheduledJobConsts.BroadcastNotificationWatchdogJobType,
                    "0 */15 * * * ?", "绕过 AppService 落的行", false);
                await _jobRepository.InsertAsync(job, autoSave: true);
                seededWatchdogId = job.Id;
            });

            // UpdateScheduledJobDto 不含 JobType（不可改），守卫按库里行的 JobType 判定
            var update = await Should.ThrowAsync<BusinessException>(() => _appService.UpdateAsync(seededWatchdogId,
                new UpdateScheduledJobDto
                {
                    Name = "seeded-watchdog",
                    CronExpression = "0 */10 * * * ?",
                    Description = "改 cron"
                }));
            update.Code.ShouldBe(AbpAdminDomainErrorCodes.HostSideOnly);

            var enable = await Should.ThrowAsync<BusinessException>(
                () => _appService.SetEnabledAsync(seededWatchdogId, true));
            enable.Code.ShouldBe(AbpAdminDomainErrorCodes.HostSideOnly);

            // 4) Trigger：禁用检查先于 Host-only 守卫（禁用行会先撞 ScheduledJobDisabled），
            //    故用一行启用状态的 watchdog 行验证守卫本身
            var enabledWatchdogId = Guid.Empty;
            await WithUnitOfWorkAsync(async () =>
            {
                var job = new ScheduledJob(
                    Guid.NewGuid(), tenantId, "seeded-watchdog-enabled",
                    ScheduledJobConsts.BroadcastNotificationWatchdogJobType,
                    "0 */15 * * * ?", "启用状态的看门狗", true);
                await _jobRepository.InsertAsync(job, autoSave: true);
                enabledWatchdogId = job.Id;
            });

            var trigger = await Should.ThrowAsync<BusinessException>(
                () => _appService.TriggerAsync(enabledWatchdogId));
            trigger.Code.ShouldBe(AbpAdminDomainErrorCodes.HostSideOnly);

            // 普通作业在租户侧照常可用（守卫只拦白名单）
            var normal = await _appService.CreateAsync(new CreateScheduledJobDto
            {
                Name = "tenant-normal-job",
                JobType = "AbpAdmin.ScheduledJobExecutionCleanup",
                CronExpression = "0 0 3 * * ?",
                IsEnabled = false,
                Description = "普通类型"
            });
            normal.Id.ShouldNotBe(Guid.Empty);
        }
    }
}
