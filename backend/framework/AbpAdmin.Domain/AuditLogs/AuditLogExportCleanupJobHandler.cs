using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Volo.Abp.AuditLogging;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 审计导出文件清理（T3.3 第 6 步，JobType = AbpAdmin.AuditLogExportCleanup）。
/// 删除超过保留期的导出记录（AbpAuditLogExcelFiles）及其 blob。
/// 只认 audit-logs- 前缀的文件名：AuditLogExcelFile 表同时被用户导出（UserExportJob，
/// blob 在另一个容器）复用，靠文件名前缀区分归属，避免删错行、清错容器。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class AuditLogExportCleanupJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = "AbpAdmin.AuditLogExportCleanup";

    /// <summary>导出文件保留天数。下载链接本就是短期有效，7 天足够。</summary>
    public const int RetentionDays = 7;

    private const string AuditExportFileNamePrefix = "audit-logs-";

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:AuditLogExportCleanup";

    private readonly IRepository<AuditLogExcelFile, Guid> _excelFileRepository;
    private readonly IBlobContainer<AuditLogExportBlobContainer> _blobContainer;
    private readonly IClock _clock;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;

    public AuditLogExportCleanupJobHandler(
        IRepository<AuditLogExcelFile, Guid> excelFileRepository,
        IBlobContainer<AuditLogExportBlobContainer> blobContainer,
        IClock clock,
        IDataFilter dataFilter,
        ICurrentTenant currentTenant)
    {
        _excelFileRepository = excelFileRepository;
        _blobContainer = blobContainer;
        _clock = clock;
        _dataFilter = dataFilter;
        _currentTenant = currentTenant;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        var cutoff = _clock.Now.AddDays(-RetentionDays);

        // NV-27：查询关过滤器全租户清扫（宿主上下文默认只见 TenantId IS NULL 行，租户用户的
        // 审计导出文件永不过期）。blob 删除另需按行切租户：容器 IsMultiTenant=true 时 blob
        // 路径跟 CurrentTenant 走，过滤器禁用不影响它——不切就删到 host 路径上，租户 blob 残留。
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var expiredFiles = (await _excelFileRepository.GetListAsync(
                    x => x.CreationTime < cutoff && x.FileName.StartsWith(AuditExportFileNamePrefix),
                    cancellationToken: context.CancellationToken))
                .ToList();

            foreach (var file in expiredFiles)
            {
                using (_currentTenant.Change(file.TenantId))
                {
                    // blob 名就是记录 Id（AuditLogExportJob 的写入约定）
                    await _blobContainer.DeleteAsync(file.Id.ToString(), cancellationToken: context.CancellationToken);
                }
                await _excelFileRepository.DeleteAsync(file, cancellationToken: context.CancellationToken);
            }
        }
    }
}
