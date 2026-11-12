using System;
using System.Threading.Tasks;
using AbpAdmin.AuditLogs;
using AbpAdmin.ClientErrors;
using AbpAdmin.HttpApi.ClientErrors;
using AbpAdmin.Identity;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IRepository<UserExcelFile, Guid> _excelFileRepository;
    private readonly IBlobContainer<UserExportBlobContainer> _blobContainer;
    private readonly IPermissionChecker _permissionChecker;

    public UserExportController(
        IIdentityUserRepository userRepository,
        IUserExcelBuilder excelBuilder,
        IRepository<UserExcelFile, Guid> excelFileRepository,
        IBlobContainer<UserExportBlobContainer> blobContainer,
        IPermissionChecker permissionChecker)
    {
        _userRepository = userRepository;
        _excelBuilder = excelBuilder;
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
            throw new Volo.Abp.BusinessException("AbpAdmin:Export:SyncThresholdExceeded");
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
            return ClientErrorActionResults.EmptyForbidden();
        }

        var bytes = await _blobContainer.GetAllBytesAsync(id.ToString());

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            excelFile.FileName);
    }
}
