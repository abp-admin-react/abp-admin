using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AbpAdmin.AuditLogs;
using AbpAdmin.Identity;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniExcelLibs;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AbpAdmin.Controllers;

[Route("api/app/identity-user-admin")]
public class UserExportController : AbpController
{
    private readonly IIdentityUserRepository _userRepository;
    private readonly IUserExcelBuilder _excelBuilder;
    private readonly IUserImportTemplateBuilder _importTemplateBuilder;
    private readonly IRepository<UserExcelFile, Guid> _excelFileRepository;
    private readonly IBlobContainer<UserExportBlobContainer> _blobContainer;
    private readonly IPermissionChecker _permissionChecker;

    public UserExportController(
        IIdentityUserRepository userRepository,
        IUserExcelBuilder excelBuilder,
        IUserImportTemplateBuilder importTemplateBuilder,
        IRepository<UserExcelFile, Guid> excelFileRepository,
        IBlobContainer<UserExportBlobContainer> blobContainer,
        IPermissionChecker permissionChecker)
    {
        _userRepository = userRepository;
        _excelBuilder = excelBuilder;
        _importTemplateBuilder = importTemplateBuilder;
        _excelFileRepository = excelFileRepository;
        _blobContainer = blobContainer;
        _permissionChecker = permissionChecker;
    }

    /// <summary>
    /// 同步导出用户。仅当结果集 ≤ 1000 条时可用，超过请走 AppService 的 EnqueueExportAsync（转后台作业）。
    /// Excel 构建复用 IUserExcelBuilder（与异步导出同一路径），控制器只做阈值判断与文件下发。
    /// </summary>
    [HttpGet("export")]
    [Authorize(AbpAdminPermissions.Identity.Users.Export)]
    public async Task<IActionResult> ExportAsync([FromQuery] string? filter)
    {
        var count = await _userRepository.GetCountAsync(filter);

        if (count > AuditLogExportConsts.SyncThreshold)
        {
            return BadRequest(new
            {
                error = $"结果集 {count} 条超过同步导出阈值 {AuditLogExportConsts.SyncThreshold} 条，请使用异步导出。"
            });
        }

        // 导出权限与明文查看权限（Update）独立授予：仅有前者时邮箱/手机号列脱敏输出（安全审查 H1）
        var maskSensitive = !await _permissionChecker.IsGrantedAsync(IdentityPermissions.Users.Update);

        var bytes = await _excelBuilder.BuildAsync(
            filter,
            maxResultCount: (int)count,
            maskSensitive,
            HttpContext.RequestAborted);
        var fileName = $"users-{Clock.Now:yyyyMMdd-HHmmss}.xlsx";

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    /// <summary>
    /// 下载导入模板。表头 + 示例行 + 数据验证下拉：
    /// 布尔列（是否启用/是否外部用户）硬校验「是/否」；角色/组织单元咨询式下拉（参考数据见隐藏 sheet）。
    /// 生成逻辑在 IUserImportTemplateBuilder（ClosedXML）；表头列名与导入解析共用 UserImportColumnNames。
    /// </summary>
    [HttpGet("import-template")]
    [Authorize(AbpAdminPermissions.Identity.Users.Import)]
    public async Task<IActionResult> GetImportTemplate()
    {
        var bytes = await _importTemplateBuilder.BuildAsync();

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "user-import-template.xlsx");
    }

    /// <summary>
    /// 下载导出文件（按 ID）。
    /// </summary>
    [HttpGet("export-file/{id}")]
    [Authorize(AbpAdminPermissions.Identity.Users.Export)]
    public async Task<IActionResult> GetExportFileAsync(Guid id)
    {
        var excelFile = await _excelFileRepository.GetAsync(id);

        // 跨用户不可下别人的导出结果
        if (excelFile.CreatorId != CurrentUser.GetId())
        {
            return Forbid();
        }

        var bytes = await _blobContainer.GetAllBytesAsync(id.ToString());

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelFile.FileName);
    }

    /// <summary>
    /// 下载导入失败明细文件。
    /// </summary>
    [HttpGet("import-failure-report/{id}")]
    [Authorize(AbpAdminPermissions.Identity.Users.Import)]
    public async Task<IActionResult> GetImportFailureReportAsync(Guid id)
    {
        var excelFile = await _excelFileRepository.GetAsync(id);

        // 跨用户不可下别人的导入结果
        if (excelFile.CreatorId != CurrentUser.GetId())
        {
            return Forbid();
        }

        var bytes = await _blobContainer.GetAllBytesAsync(id.ToString());

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelFile.FileName);
    }
}
