using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using AbpAdmin.Settings;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.TenantManagement;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 过期审计日志清理（T3.3 第 6 步，JobType = AbpAdmin.AuditLogCleanup）。
/// 由 T2.1 的 ExpiredAuditLogDeleterWorker（固定间隔 worker）迁移而来，业务逻辑不变：
/// 两层开关——host 侧总开关 + 租户侧保留天数设置。
/// host 级作业（TenantId 为 null）保持原 worker 语义：遍历所有租户 + host 各自清理；
/// 租户级作业只清理本租户。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class AuditLogCleanupJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = "AbpAdmin.AuditLogCleanup";

    /// <summary>分批删除的批大小：一条 DELETE 打穿几十万行会导致锁表，按批删。</summary>
    public const int DeleteBatchSize = 1000;

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:AuditLogCleanup";

    private readonly ISettingProvider _settingProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IClock _clock;

    public AuditLogCleanupJobHandler(
        ISettingProvider settingProvider,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IAuditLogRepository auditLogRepository,
        ITenantRepository tenantRepository,
        IClock clock)
    {
        _settingProvider = settingProvider;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _auditLogRepository = auditLogRepository;
        _tenantRepository = tenantRepository;
        _clock = clock;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        // 1. host 侧总开关：关闭则整体跳过，租户设置无效
        var hostEnabled = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Audit.ExpiredItemDeletionEnabled);
        if (hostEnabled != "true")
        {
            return;
        }

        if (context.TenantId.HasValue)
        {
            // 租户级作业：只清理本租户（当前租户上下文已由调度器切好）
            await DeleteExpiredForCurrentTenantAsync(context.CancellationToken);
            return;
        }

        // 2. host 级作业保持原 worker 语义：遍历租户（含 host），各自切租户上下文清理
        var tenants = await _tenantRepository.GetListAsync();
        foreach (var tenant in tenants)
        {
            using (_currentTenant.Change(tenant.Id))
            {
                await DeleteExpiredForCurrentTenantAsync(context.CancellationToken);
            }
        }

        // host 侧（tenantId = null）也要清理
        using (_currentTenant.Change(null))
        {
            await DeleteExpiredForCurrentTenantAsync(context.CancellationToken);
        }
    }

    private async Task DeleteExpiredForCurrentTenantAsync(CancellationToken cancellationToken)
    {
        var daysStr = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Audit.ExpiredItemDeletionPeriodDays);
        if (!int.TryParse(daysStr, out var days) || days <= 0)
        {
            return;
        }

        var cutoff = _clock.Now.AddDays(-days);

        // 外层 UoW 由调度器开好（包住 handler 那次调用）。这里是逐租户/分批的
        // 独立提交单元，沿用原 worker 的写法：每租户一个 requiresNew 事务，
        // 按批删除（DeleteManyAsync 批量删，与 ScheduledJobExecutionCleanupJobHandler 同风格），
        // 避免一条 DELETE 打穿几十万行导致锁表。
        // 取消语义：取消检查发生在批边界，但事务粒度是"租户"而非"批"——
        // uow.SaveChangesAsync 只是把已删批发进本租户事务，租户中途取消时
        // CompleteAsync 不会执行、该租户本轮全部已删批回滚；此前已 Complete 的租户不受影响。
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var expiredLogs = await _auditLogRepository.GetListAsync(
                    maxResultCount: DeleteBatchSize,
                    endTime: cutoff,
                    cancellationToken: cancellationToken);

                if (expiredLogs.Count == 0)
                {
                    break;
                }

                // 「已处理」标记是 AuditLog 行内映射列，随行删除——侧表时代"先删标记再删日志"
                // 的孤儿清理逻辑已随侧表退役
                var expiredLogIds = expiredLogs.Select(x => x.Id).ToList();

                await _auditLogRepository.DeleteManyAsync(
                    expiredLogIds,
                    autoSave: false,
                    cancellationToken: cancellationToken);

                await uow.SaveChangesAsync(cancellationToken);

                if (expiredLogs.Count < DeleteBatchSize)
                {
                    break;
                }
            }

            await uow.CompleteAsync(cancellationToken);
        }
    }
}
