using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using AbpAdmin.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.AuditLogs;

/* T2.1 审计日志对标：过期清理测试（03-batch2-pro-parity.md T2.1 验收标准：
 * 「ExpiredAuditLogDeleterTests.cs 覆盖两层开关」）。
 * T3.3 已把 ExpiredAuditLogDeleterWorker 迁移为 AuditLogCleanupJobHandler，
 * 这里测 handler 本体：
 * - host 总开关开 + 保留 1 天：3 天前的日志被删，当天的保留；
 * - host 总开关关：即使天数设置存在，一条都不删。
 * 设置在共享测试库里全局生效，每个用例 finally 恢复默认并删除插入的行，避免污染其他用例。
 */
public abstract class ExpiredAuditLogDeleterTests<TStartupModule> : AuditLogTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ISettingManager _settingManager;

    protected ExpiredAuditLogDeleterTests()
    {
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    private IScheduledJobHandler GetCleanupHandler()
    {
        return ServiceProvider
            .GetServices<IScheduledJobHandler>()
            .First(x => x.JobType == AuditLogCleanupJobHandler.JobTypeName);
    }

    private async Task<bool> LogExistsAsync(Guid id)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            return await _auditLogRepository.FindAsync(id) != null;
        });
    }

    private Task DeleteLogQuietlyAsync(Guid id)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            if (await _auditLogRepository.FindAsync(id) != null)
            {
                await _auditLogRepository.DeleteAsync(id);
            }
        });
    }

    [Fact]
    public async Task Should_Delete_Expired_Logs_When_Host_Switch_Enabled()
    {
        // Arrange - host 总开关开，保留天数设为 1
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionEnabled, "true");
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionPeriodDays, "1");

        var marker = $"/cleanup-test/{Guid.NewGuid():N}";
        var expiredLog = await CreateAuditLogAsync(DateTime.UtcNow.AddDays(-3), marker);
        var recentLog = await CreateAuditLogAsync(DateTime.UtcNow, marker);

        try
        {
            // Act - 手动触发 host 级清理作业（TenantId 为 null = host 级语义）
            await GetCleanupHandler().ExecuteAsync(new ScheduledJobContext
            {
                ScheduledJobId = Guid.NewGuid(),
                TenantId = null,
                JobType = AuditLogCleanupJobHandler.JobTypeName
            });

            // Assert - 3 天前的被删，当天的保留
            (await LogExistsAsync(expiredLog.Id)).ShouldBeFalse();
            (await LogExistsAsync(recentLog.Id)).ShouldBeTrue();
        }
        finally
        {
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionPeriodDays, "30");
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionEnabled, "true");
            await DeleteLogQuietlyAsync(recentLog.Id);
        }
    }

    [Fact]
    public async Task Should_Not_Delete_When_Host_Switch_Disabled()
    {
        // Arrange - host 侧总开关关闭，天数设置存在也不生效
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionEnabled, "false");
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionPeriodDays, "1");

        var marker = $"/cleanup-test/{Guid.NewGuid():N}";
        var expiredLog = await CreateAuditLogAsync(DateTime.UtcNow.AddDays(-3), marker);

        try
        {
            // Act
            await GetCleanupHandler().ExecuteAsync(new ScheduledJobContext
            {
                ScheduledJobId = Guid.NewGuid(),
                TenantId = null,
                JobType = AuditLogCleanupJobHandler.JobTypeName
            });

            // Assert - 一条都不删
            (await LogExistsAsync(expiredLog.Id)).ShouldBeTrue();
        }
        finally
        {
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionEnabled, "true");
            await _settingManager.SetGlobalAsync(AbpAdminSettings.Audit.ExpiredItemDeletionPeriodDays, "30");
            await DeleteLogQuietlyAsync(expiredLog.Id);
        }
    }
}
