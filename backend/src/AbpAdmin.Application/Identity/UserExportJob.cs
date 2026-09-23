using System;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace AbpAdmin.Identity;

[BackgroundJobName("abpadmin.user-export")]
public class UserExportJobArgs
{
    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = default!;

    public string? Filter { get; set; }

    /// <summary>入队用户无 AbpIdentity.Users.Update 权限时为 true：Excel 邮箱/手机号列脱敏输出。</summary>
    public bool MaskSensitive { get; set; } = true;
}

public class UserExportJob : AsyncBackgroundJob<UserExportJobArgs>, ITransientDependency
{
    /// <summary>
    /// 后台导出行数硬上限：替代原 int.MaxValue——超大租户全量导出会把用户实体、Excel 行、
    /// byte[] 同时钉在内存里（OOM 风险）。超限导出前 N 行并在作业日志留警告，
    /// 需要完整数据时应按筛选条件分批导出。
    /// </summary>
    public const int MaxExportRows = 50_000;

    private readonly IUserExcelBuilder _excelBuilder;
    private readonly IIdentityUserRepository _userRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<UserExcelFile, Guid> _excelFileRepository;
    private readonly IBlobContainer<UserExportBlobContainer> _blobContainer;
    private readonly IEmailSender _emailSender;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;
    private readonly ILogger<UserExportJob> _logger;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public UserExportJob(
        IUserExcelBuilder excelBuilder,
        IIdentityUserRepository userRepository,
        ICurrentTenant currentTenant,
        IRepository<UserExcelFile, Guid> excelFileRepository,
        IBlobContainer<UserExportBlobContainer> blobContainer,
        IEmailSender emailSender,
        IStringLocalizer<AbpAdminResource> localizer,
        ILogger<UserExportJob> logger,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _excelBuilder = excelBuilder;
        _userRepository = userRepository;
        _currentTenant = currentTenant;
        _excelFileRepository = excelFileRepository;
        _blobContainer = blobContainer;
        _emailSender = emailSender;
        _localizer = localizer;
        _logger = logger;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public override async Task ExecuteAsync(UserExportJobArgs args)
    {
        using (_currentTenant.Change(args.TenantId))
        {
            // 超限先记警告再截断导出（COUNT 走筛选索引，后台作业可承受）
            var totalCount = await _userRepository.GetCountAsync(args.Filter);
            if (totalCount > MaxExportRows)
            {
                _logger.LogWarning(
                    "User export matched {TotalCount} rows which exceeds the export cap of {MaxExportRows}; only the first {MaxExportRows} rows will be exported. Filter: {Filter}",
                    totalCount, MaxExportRows, MaxExportRows, args.Filter);
            }

            var bytes = await _excelBuilder.BuildAsync(args.Filter, maxResultCount: MaxExportRows, args.MaskSensitive);

            var fileName = $"users-{_clock.Now:yyyyMMdd-HHmmss}.xlsx";
            var excelFileId = _guidGenerator.Create();
            var blobName = excelFileId.ToString();

            // 用户导出文件记录走 Identity 模块自有的 UserExcelFile（不再借用审计模块实体）
            var excelFile = new UserExcelFile(
                excelFileId,
                fileName,
                args.TenantId,
                args.UserId);

            // 记录先落库（autoSave）→ blob 后存：blob 先存而插记录失败会留下永远清不掉的
            // 孤儿 blob（清理作业按 DB 记录找 blob 删）。与 AuditLogExportJob 同一模式。
            await _excelFileRepository.InsertAsync(excelFile, autoSave: true);

            try
            {
                await _blobContainer.SaveAsync(blobName, bytes);
            }
            catch (Exception ex)
            {
                // 补偿：blob 写失败回删已落库记录，重试完整重建"记录 + blob"
                Logger.LogWarning(ex,
                    "Saving user export blob {BlobName} failed, compensating by deleting the inserted record {RecordId}.",
                    blobName, excelFileId);
                try
                {
                    await _excelFileRepository.DeleteAsync(excelFile, autoSave: true);
                }
                catch (Exception compensateEx)
                {
                    Logger.LogWarning(compensateEx,
                        "Compensating delete of user export record {RecordId} also failed; it may need manual cleanup.",
                        excelFileId);
                }

                throw;
            }

            // 邮件单独兜底：文件已生成并可下载，SMTP 抖动不应判整个作业失败（与 AuditLogExportJob 同策略）
            try
            {
                await _emailSender.SendAsync(
                    args.Email,
                    _localizer["UserExport:EmailSubject"],
                    _localizer["UserExport:EmailBody", fileName]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "User export succeeded but the notification email failed. File {FileName} is still downloadable from the export list.",
                    fileName);
            }
        }
    }
}
