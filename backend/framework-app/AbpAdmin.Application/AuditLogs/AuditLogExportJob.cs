using System;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Timing;

namespace AbpAdmin.AuditLogs;

[BackgroundJobName("abpadmin.audit-log-export")]
public class AuditLogExportJobArgs
{
    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = default!;

    public GetAuditLogListInput Filter { get; set; } = default!;
}

public class AuditLogExportJob : AsyncBackgroundJob<AuditLogExportJobArgs>, ITransientDependency
{
    private readonly AuditLogExcelBuilder _excelBuilder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<AuditLogExcelFile, Guid> _auditLogExcelFileRepository;
    private readonly IBlobContainer<AuditLogExportBlobContainer> _blobContainer;
    private readonly IEmailSender _emailSender;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;
    private readonly ILogger<AuditLogExportJob> _logger;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public AuditLogExportJob(
        AuditLogExcelBuilder excelBuilder,
        ICurrentTenant currentTenant,
        IRepository<AuditLogExcelFile, Guid> auditLogExcelFileRepository,
        IBlobContainer<AuditLogExportBlobContainer> blobContainer,
        IEmailSender emailSender,
        IStringLocalizer<AbpAdminResource> localizer,
        ILogger<AuditLogExportJob> logger,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _excelBuilder = excelBuilder;
        _currentTenant = currentTenant;
        _auditLogExcelFileRepository = auditLogExcelFileRepository;
        _blobContainer = blobContainer;
        _emailSender = emailSender;
        _localizer = localizer;
        _logger = logger;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public override async Task ExecuteAsync(AuditLogExportJobArgs args)
    {
        using (_currentTenant.Change(args.TenantId))
        {
            // 后台分支同样有硬上限（AuditLogExportConsts.AsyncMaxRows）：审计日志只增不减，
            // 无上限的全量物化会打爆进程内存。先取命中条数判断是否截断，
            // 超限时只导出前 AsyncMaxRows 条，并在完成邮件中注明。
            // 计数必须走 Builder.CountAsync：UnhandledErrorOnly 的排除口径在 Builder 侧
            // 生效（仓储计数不认识该筛选），直接 GetCountByInputAsync 会拿未过滤总数，
            // 误判截断并在邮件里谎报「仅导出前 N 条」。
            var totalCount = await _excelBuilder.CountAsync(args.Filter);
            var truncated = totalCount > AuditLogExportConsts.AsyncMaxRows;
            var bytes = await _excelBuilder.BuildAsync(
                args.Filter,
                maxResultCount: truncated ? AuditLogExportConsts.AsyncMaxRows : (int)totalCount);

            var fileName = $"audit-logs-{_clock.Now:yyyyMMdd-HHmmss}.xlsx";
            var excelFileId = _guidGenerator.Create();
            var blobName = excelFileId.ToString();

            var excelFile = new AuditLogExcelFile(
                excelFileId,
                fileName,
                args.TenantId,
                args.UserId);

            // 写入顺序：记录先落、blob 后存。清理作业按 DB 记录找 blob 删，
            // "有 blob 必有记录"，失败重试不会留下清不掉的孤儿 blob；
            // 反过来（blob 先落、插记录失败）会留下无记录的孤儿 blob，永远清不掉。
            // autoSave:true 让记录立即落库：下方 blob 失败的补偿回删需要它已真实写入，
            // 不能依赖外层 UoW 的提交时机（后台作业路径上是否有环境 UoW 不由本类控制）。
            await _auditLogExcelFileRepository.InsertAsync(excelFile, autoSave: true);

            try
            {
                await _blobContainer.SaveAsync(blobName, bytes);
            }
            catch (Exception ex)
            {
                // 补偿：blob 写失败（如存储瞬时故障）时回删已落库的记录。
                // 不回删的话重试会再插一条新记录，旧记录永远等不到它的 blob——
                // 导出列表里多一条点不开的死链。回删后重抛，ABP 按退避重试整个导出，
                // 重试完整重建"记录 + blob"，最终仍收敛到一记录对一 blob。
                _logger.LogWarning(ex,
                    "Saving audit log export blob {BlobName} failed, compensating by deleting the inserted record {RecordId}.",
                    blobName, excelFileId);
                try
                {
                    await _auditLogExcelFileRepository.DeleteAsync(excelFile, autoSave: true);
                }
                catch (Exception compensateEx)
                {
                    // 补偿也失败（DB 紧接着故障）：记日志后仍重抛原始异常，
                    // 不让补偿失败掩盖根因；重试时重复记录由上方的唯一性收敛语义兜底
                    _logger.LogWarning(compensateEx,
                        "Compensating delete of audit log export record {RecordId} also failed; it may need manual cleanup.",
                        excelFileId);
                }

                throw;
            }

            // 邮件单独兜底：SMTP 抖动不应判整个作业失败——ABP 会按退避重试整个导出，
            // 造成重复 blob + 重复记录 + 用户收到多封邮件。文件已生成，
            // 用户可从导出文件列表自行下载，这里只记日志。
            try
            {
                var body = truncated
                    ? _localizer["AuditLogExport:EmailBodyTruncated", AuditLogExportConsts.AsyncMaxRows, fileName]
                    : _localizer["AuditLogExport:EmailBody", fileName];

                await _emailSender.SendAsync(
                    args.Email,
                    _localizer["AuditLogExport:EmailSubject"],
                    body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Audit log export succeeded but the notification email failed. File {FileName} is still downloadable from the export list.",
                    fileName);
            }
        }
    }
}
