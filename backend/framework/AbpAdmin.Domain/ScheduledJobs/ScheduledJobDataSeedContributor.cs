using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.AuditLogs;
using AbpAdmin.Files;
using AbpAdmin.Gdpr;
using AbpAdmin.Localization;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 定时作业种子（T3.3 第 10 步）：为 7 个内置 handler 各建一条配置记录。
/// 全部默认停用（IsEnabled = false）——这是刻意的：让部署者主动决定开哪些、
/// 什么时间跑，而不是部署完就有 7 个作业在跑。种子只负责"把选项摆出来"。
/// 幂等：按 Name 查存在则跳过（DbMigrator 可能重复运行；多租户过滤器使
/// 每个租户上下文只看到自己租户的记录，host 与租户各自种一份）。
/// </summary>
public class ScheduledJobDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<ScheduledJob, System.Guid> _jobRepository;
    private readonly IGuidGenerator _guidGenerator;

    public ScheduledJobDataSeedContributor(
        IRepository<ScheduledJob, System.Guid> jobRepository,
        IGuidGenerator guidGenerator)
    {
        _jobRepository = jobRepository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var existingNames = (await _jobRepository.GetListAsync())
            .Select(x => x.Name)
            .ToHashSet();

        foreach (var (name, cron, description) in Defaults)
        {
            if (existingNames.Contains(name))
            {
                continue;
            }

            await _jobRepository.InsertAsync(new ScheduledJob(
                _guidGenerator.Create(),
                context.TenantId,
                name,
                jobType: name,
                cronExpression: cron,
                description: description,
                isEnabled: false));
        }
    }

    private static readonly (string Name, string Cron, string Description)[] Defaults =
    [
        (AuditLogCleanupJobHandler.JobTypeName, "0 0 3 * * ?", "每天凌晨 3 点清理过期审计日志"),
        (AuditLogExportCleanupJobHandler.JobTypeName, "0 30 3 * * ?", "每天凌晨 3:30 清理过期审计导出文件"),
        (LanguageSyncJobHandler.JobTypeName, "0 0 4 * * ?", "每天凌晨 4 点同步语言表（host 级）"),
        (GdprRequestCleanupJobHandler.JobTypeName, "0 0 5 ? * SUN", "每周日凌晨 5 点清理过期 GDPR 请求"),
        (ScheduledJobExecutionCleanupJobHandler.JobTypeName, "0 0 2 * * ?", "每天凌晨 2 点清理过期执行历史"),
        (ThumbnailBackfillJobHandler.JobTypeName, "0 */30 * * * ?", "每 30 分钟回填缺失的缩略图"),
        // 广播断链看门狗：handler 在 Application 层（要构造 BroadcastNotificationJobArgs，
        // Domain 不能反向引用），JobType 收口在 Domain.Shared 的 ScheduledJobConsts，
        // 与 handler、HostOnlyScheduledJobTypes 共用同一常量（round4 前是三处字面量靠注释同步）。
        (ScheduledJobConsts.BroadcastNotificationWatchdogJobType, "0 */15 * * * ?", "每 15 分钟巡检停滞的广播并按游标续跑"),
    ];
}
