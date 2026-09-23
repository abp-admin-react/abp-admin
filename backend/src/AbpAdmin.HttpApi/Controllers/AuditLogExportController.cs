using System;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using AbpAdmin.Permissions;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AuditLogging;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace AbpAdmin.AuditLogs;

[Route("api/app/audit-log")]
[Authorize(AbpAdminPermissions.AuditLogs.Export)]
public class AuditLogExportController : AbpController
{
    private readonly IRepository<AuditLogExcelFile, Guid> _auditLogExcelFileRepository;
    private readonly IBlobContainer<AuditLogExportBlobContainer> _blobContainer;
    private readonly IAuditLogExcelBuilder _excelBuilder;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;
    private readonly IOperationRateLimitingChecker _rateLimitingChecker;

    public AuditLogExportController(
        IRepository<AuditLogExcelFile, Guid> auditLogExcelFileRepository,
        IBlobContainer<AuditLogExportBlobContainer> blobContainer,
        IAuditLogExcelBuilder excelBuilder,
        IStringLocalizer<AbpAdminResource> localizer,
        IOperationRateLimitingChecker rateLimitingChecker)
    {
        _auditLogExcelFileRepository = auditLogExcelFileRepository;
        _blobContainer = blobContainer;
        _excelBuilder = excelBuilder;
        _localizer = localizer;
        _rateLimitingChecker = rateLimitingChecker;
    }

    /// <summary>
    /// 同步导出审计日志。仅当结果集 ≤ 1000 条时可用，超过请走 AppService 的 ExportAsync（转后台作业）。
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportAsync([FromQuery] GetAuditLogListInput input)
    {
        // round3 补限流：Controller 不经 DI 拦截器（特性只对 AppService 生效），同步导出这条
        // 贵路径此前零限流——与异步路径（AuditLogAppService.EnqueueExportAsync 特性）共用
        // AuditLogExport 策略（按当前用户 1 天 10 次，租户隔离），两侧合计计入同一配额。
        // 超限抛 AbpAdminOperationRateLimitingException，交给 ABP 异常处理返回 429 + Retry-After。
        await _rateLimitingChecker.CheckAsync(
            OperationRateLimitingPolicyNames.AuditLogExport,
            CurrentUser.Id.ToString());

        var count = await _excelBuilder.CountAsync(input);

        if (count > AuditLogExportConsts.SyncThreshold)
        {
            return BadRequest(new
            {
                error = _localizer["AuditLogExport:SyncThresholdExceeded", count, AuditLogExportConsts.SyncThreshold]
            });
        }

        var bytes = await _excelBuilder.BuildAsync(input, maxResultCount: (int)count);
        var fileName = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpGet("export-file/{id}")]
    public async Task<IActionResult> GetExportFileAsync(Guid id)
    {
        // FindAsync + 显式 404：仓储 GetAsync 抛的 EntityNotFoundException 在 MVC 控制器
        //（非应用服务）链路上不会被转成 404，渗透测试实测不存在的 id 直接 500。
        var excelFile = await _auditLogExcelFileRepository.FindAsync(id);
        if (excelFile == null)
        {
            return NotFound();
        }

        // 跨用户不可下别人的导出结果
        if (excelFile.CreatorId != CurrentUser.GetId())
        {
            return Forbid();
        }

        byte[] bytes;
        try
        {
            bytes = await _blobContainer.GetAllBytesAsync(id.ToString());
        }
        catch (AbpException ex)
        {
            // 记录在、blob 已丢（如被存储端手工清理）：明确 404 死链语义，
            // 与导出作业「有 blob 必有记录」的补偿语义对齐，这侧补的是反向兜底。
            // ABP 容器对缺失 blob 抛的是裸 AbpException（源码 TODO 承认缺专用类型），
            // 捕获范围以它为限：其他异常（瞬时存储故障等）保持 500/重试语义不伪装成死链。
            Logger.LogWarning(ex,
                "Export file record {RecordId} exists but its blob is missing; returning 404.", id);
            return NotFound();
        }

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelFile.FileName);
    }
}
