using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using MiniExcelLibs;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using Volo.Abp.Uow;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Identity;

[Authorize(IdentityPermissions.Users.Update)]
public class IdentityUserAdminAppService : AbpAdminAppService, IIdentityUserAdminAppService
{
    /// <summary>批量 2FA 状态查询的 id 数上限（对齐前端最大页大小的量级，防被当全表探测接口）。</summary>
    private const int TwoFactorStatusBatchLimit = 200;

    private readonly IdentityUserManager _userManager;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<Volo.Abp.Identity.IdentityUser, Guid> _userRepository;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IBlobContainer<UserExportBlobContainer> _blobContainer;
    private readonly IRepository<UserExcelFile, Guid> _excelFileRepository;
    private readonly IPermissionChecker _permissionChecker;

    public IdentityUserAdminAppService(
        IdentityUserManager userManager,
        IIdentityRoleRepository roleRepository,
        IRepository<Volo.Abp.Identity.IdentityUser, Guid> userRepository,
        IOrganizationUnitRepository organizationUnitRepository,
        IBackgroundJobManager backgroundJobManager,
        IBlobContainer<UserExportBlobContainer> blobContainer,
        IRepository<UserExcelFile, Guid> excelFileRepository,
        IPermissionChecker permissionChecker)
    {
        _userManager = userManager;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _organizationUnitRepository = organizationUnitRepository;
        _permissionChecker = permissionChecker;
        _backgroundJobManager = backgroundJobManager;
        _blobContainer = blobContainer;
        _excelFileRepository = excelFileRepository;
    }

    [OperationLog("身份管理", "锁定用户", BizNo = "{{id}}", Success = "锁定了用户 {{user(id)}}")]
    public virtual async Task LockAsync(Guid id)
    {
        var user = await _userManager.GetByIdAsync(id);
        ThrowIfFailed(await _userManager.SetLockoutEnabledAsync(user, true));
        ThrowIfFailed(await _userManager.SetLockoutEndDateAsync(
            user,
            new DateTimeOffset(Clock.Now).AddYears(AbpAdmin.Account.AbpAdminAccountConsts.LockoutDurationYears)));
    }

    [OperationLog("身份管理", "解锁用户", BizNo = "{{id}}", Success = "解锁了用户 {{user(id)}}")]
    public virtual async Task UnlockAsync(Guid id)
    {
        var user = await _userManager.GetByIdAsync(id);
        ThrowIfFailed(await _userManager.SetLockoutEndDateAsync(user, null));
    }

    /// <summary>
    /// 异步导出用户。转后台作业，完成后发邮件通知。
    /// ABP 动态 API 路由：POST /api/app/identity-user-admin/enqueue-export
    /// </summary>
    [Authorize(AbpAdminPermissions.Identity.Users.Export)]
    [OperationLog("身份管理", "导出用户", Success = "发起了用户导出（筛选：{{filter}}）")]
    public virtual async Task<UserExportResultDto> EnqueueExportAsync(string? filter)
    {
        // 导出权限(Export)与明文查看权限(Update)是两个独立授权：只有前者时 Excel 脱敏输出，
        // 与响应序列化脱敏同一门禁（安全审查 H1）。判定必须在此处做——后台作业无请求上下文。
        var maskSensitive = !await _permissionChecker.IsGrantedAsync(IdentityPermissions.Users.Update);

        await _backgroundJobManager.EnqueueAsync(
            new UserExportJobArgs
            {
                TenantId = CurrentTenant.Id,
                UserId = CurrentUser.GetId(),
                Email = CurrentUser.Email ?? string.Empty,
                Filter = filter,
                MaskSensitive = maskSensitive
            });
        return UserExportResultDto.Queued();
    }

    /// <summary>
    /// 导入用户。逐行校验，部分成功语义。
    /// ABP 动态 API 路由：POST /api/app/identity-user-admin/import
    /// </summary>
    [Authorize(AbpAdminPermissions.Identity.Users.Import)]
    [OperationLog("身份管理", "导入用户", Success = "上传了用户导入文件（{{file.fileName}}）")]
    public virtual async Task<UserImportResultDto> ImportAsync(IRemoteStreamContent file)
    {
        var result = new UserImportResultDto();
        var errors = new List<UserImportRowErrorDto>();
        var errorRows = new List<UserImportRowDto>();
        var rowNumber = 0;

        using var stream = file.GetStream();
        // 模板/导出产物是 xlsx，前端也接受 csv——按扩展名/Content-Type 分支解析器（ExcelType.CSV）
        var excelType = ResolveExcelType(file);

        // 流式物化：行数硬上限在解析期生效。先 ToList 再检查会让超大文件先把内存吃满
        //（高压缩比 xlsx / 超长 CSV 是资源耗尽面，见 AbpAdminAccountConsts 注释）
        var rows = new List<UserImportRowDto>();
        foreach (var row in MiniExcel.Query<UserImportRowDto>(stream, excelType: excelType))
        {
            rows.Add(row);
            if (rows.Count > AbpAdmin.Account.AbpAdminAccountConsts.ImportMaxRowCount)
            {
                throw new UserFriendlyException(L["UserImport:TooManyRows", AbpAdmin.Account.AbpAdminAccountConsts.ImportMaxRowCount]);
            }
        }

        result.TotalCount = rows.Count;

        // 文件级解析一次：OU 编码/显示名字典与角色名集合在行循环外构建。
        // 每行独立 requiresNew UoW 使任何跨行缓存失效——逐行全表查询会把导入拖成
        // O(行数 × OU 数)（六透镜审查 performance-High：5000 行 × 1000 OU ≈ 5000 次全表 SELECT）。
        var allUnits = await _organizationUnitRepository.GetListAsync();
        var ouIdByCode = allUnits
            .Where(ou => !string.IsNullOrWhiteSpace(ou.Code))
            .GroupBy(ou => ou.Code)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var ouIdsByDisplayName = allUnits
            .GroupBy(ou => ou.DisplayName)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(g => g.Key, g => g.Select(ou => ou.Id).ToList());
        var knownRoleNames = (await _roleRepository.GetListAsync())
            .Select(r => r.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            rowNumber++;
            try
            {
                // 每行一个独立的工作单元
                using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                await ImportSingleRowAsync(row, rowNumber, ouIdByCode, ouIdsByDisplayName, knownRoleNames);
                await uow.CompleteAsync();
                result.SucceededCount++;
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                var errorMessage = GetFriendlyErrorMessage(ex);
                errors.Add(new UserImportRowErrorDto
                {
                    RowNumber = rowNumber,
                    UserName = row.UserName,
                    ErrorMessage = errorMessage
                });
                // 与 errors 同步 Add，两者按下标一一对齐（SaveFailureReportAsync 依赖该不变式）
                errorRows.Add(row);
            }
        }

        // 只返回前 N 条错误（上限集中定义见 AbpAdminAccountConsts）
        result.Errors = errors.Take(AbpAdmin.Account.AbpAdminAccountConsts.ImportErrorDisplayLimit).ToList();

        // 生成失败明细文件
        if (errorRows.Count > 0)
        {
            result.FailureReportId = await SaveFailureReportAsync(errorRows, errors);
        }

        return result;
    }

    /// <summary>
    /// 批量获取用户双因素认证状态。Volo 开源版用户列表契约（IdentityUserDto）不含
    /// twoFactorEnabled 字段，列表页用它页级补齐——缺失时 2FA 列恒显示「否」、开关恒发 true
    /// （六透镜审查 function-High）。
    /// ABP 动态 API 路由：GET /api/app/identity-user-admin/two-factor-statuses?userIds=...&amp;userIds=...
    ///（约定路由剥离 Get 前缀，注释按实际路由标注）
    /// </summary>
    public virtual async Task<List<UserTwoFactorStatusDto>> GetTwoFactorStatusesAsync(GetUserTwoFactorStatusesInput input)
    {
        var ids = input.UserIds.Distinct().Take(TwoFactorStatusBatchLimit).ToList();
        if (ids.Count == 0)
        {
            return new List<UserTwoFactorStatusDto>();
        }

        var users = await _userRepository.GetListAsync(u => ids.Contains(u.Id));
        return users
            .Select(u => new UserTwoFactorStatusDto
            {
                UserId = u.Id,
                TwoFactorEnabled = u.TwoFactorEnabled
            })
            .ToList();
    }

    /// <summary>
    /// 获取当前登录账户状态。
    /// ABP 动态 API 路由：GET /api/app/identity-user-admin/current-account-status
    /// </summary>
    [Authorize]
    public virtual async Task<AccountStatusDto> GetCurrentAccountStatusAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());

        // 管理员强制改密
        if (user.ShouldChangePasswordOnNextLogin)
        {
            return new AccountStatusDto
            {
                ShouldChangePassword = true,
                Reason = L["AccountStatus:AdminForceChangePassword"]
            };
        }

        // 外部用户不受密码有效期影响
        if (user.IsExternal)
        {
            return new AccountStatusDto { ShouldChangePassword = false };
        }

        // 密码过期
        if (await _userManager.ShouldPeriodicallyChangePasswordAsync(user))
        {
            return new AccountStatusDto
            {
                ShouldChangePassword = true,
                Reason = L["AccountStatus:PasswordExpired"]
            };
        }

        return new AccountStatusDto { ShouldChangePassword = false };
    }

    /// <summary>
    /// 要求指定用户下次登录时修改密码。
    /// ABP 动态 API 路由：POST /api/app/identity-user-admin/{id}/require-change-password
    /// </summary>
    [Authorize(IdentityPermissions.Users.Update)]
    [OperationLog("身份管理", "强制修改密码", BizNo = "{{id}}", Success = "要求用户 {{user(id)}} 下次登录时修改密码")]
    public virtual async Task RequireChangePasswordOnNextLoginAsync(Guid id)
    {
        var user = await _userManager.GetByIdAsync(id);
        user.SetShouldChangePasswordOnNextLogin(true);
        ThrowIfFailed(await _userManager.UpdateAsync(user));
    }

    /// <summary>
    /// 导入单行用户数据：校验 → 解析组织单元 → 建用户 → 分配角色 → 挂接组织单元。
    /// OU 解析放在建用户之前，未知编码/歧义名称先行出行级错误，不产生半截用户
    /// （行内异常由独立 UoW 整体回滚，这里前移只是把错误暴露得更早更明确）。
    /// OU/角色的候选集合由 ImportAsync 在文件级解析一次后传入，本方法不做任何全表查询。
    /// </summary>
    protected virtual async Task ImportSingleRowAsync(
        UserImportRowDto row,
        int rowNumber,
        IReadOnlyDictionary<string, Guid> ouIdByCode,
        IReadOnlyDictionary<string, List<Guid>> ouIdsByDisplayName,
        IReadOnlySet<string> knownRoleNames)
    {
        await ValidateRowAsync(row, rowNumber);
        var organizationUnitIds = ResolveOrganizationUnitIds(row, rowNumber, ouIdByCode, ouIdsByDisplayName);
        var user = await CreateUserAsync(row, rowNumber);
        await AssignRolesAsync(user, row, rowNumber, knownRoleNames);
        await AssignOrganizationUnitsAsync(user, organizationUnitIds);
    }

    /// <summary>
    /// 解析导入行的组织单元列为 id 集合。分号分隔多值，同时接受组织单元编码（模板下拉提供）
    /// 与显示名（与导出列对称，导出写显示名）——显示名命中多个组织单元时要求改用编码。
    /// 已知限制：显示名含分号时会被当作多值分隔符拆开，此类值必须用编码表达（失败明细可诊断）。
    /// </summary>
    protected virtual List<Guid> ResolveOrganizationUnitIds(
        UserImportRowDto row,
        int rowNumber,
        IReadOnlyDictionary<string, Guid> ouIdByCode,
        IReadOnlyDictionary<string, List<Guid>> ouIdsByDisplayName)
    {
        if (string.IsNullOrWhiteSpace(row.OrganizationUnitCodes))
        {
            return new List<Guid>();
        }

        var tokens = row.OrganizationUnitCodes.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();
        if (tokens.Count == 0)
        {
            return new List<Guid>();
        }

        var result = new List<Guid>();
        foreach (var token in tokens)
        {
            if (ouIdByCode.TryGetValue(token, out var id))
            {
                result.Add(id);
                continue;
            }

            if (ouIdsByDisplayName.TryGetValue(token, out var matches))
            {
                if (matches.Count == 1)
                {
                    result.Add(matches[0]);
                    continue;
                }

                throw new UserFriendlyException(L["UserImport:OrganizationUnitAmbiguous", rowNumber, token]);
            }

            throw new UserFriendlyException(L["UserImport:OrganizationUnitNotFound", rowNumber, token]);
        }

        return result.Distinct().ToList();
    }

    /// <summary>
    /// 将导入用户挂接到解析好的组织单元。id 在行 UoW 内按主键取实体（并发删除时出行级错误），
    /// 不跨 UoW 复用文件级加载的实体；AddToOrganizationUnitAsync 返回 void（内部查重，重复挂接幂等）。
    /// </summary>
    protected virtual async Task AssignOrganizationUnitsAsync(
        Volo.Abp.Identity.IdentityUser user,
        IReadOnlyList<Guid> organizationUnitIds)
    {
        foreach (var organizationUnitId in organizationUnitIds)
        {
            var unit = await _organizationUnitRepository.GetAsync(organizationUnitId);
            await _userManager.AddToOrganizationUnitAsync(user, unit);
        }
    }

    /// <summary>
    /// 按文件扩展名/Content-Type 选择 MiniExcel 解析器：前端导入对话框接受 .csv，
    /// 服务端必须对称支持，否则用户按提示选择 csv 会在解析层报晦涩错误。
    /// .xls（BIFF 老格式）MiniExcel 不支持，明确拒绝而不是落进 XLSX 分支报解析异常。
    /// </summary>
    protected virtual ExcelType ResolveExcelType(IRemoteStreamContent file)
    {
        var extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(L["UserImport:UnsupportedFormat", file.FileName!]);
        }

        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ExcelType.CSV;
        }

        if (file.ContentType?.Contains("csv", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ExcelType.CSV;
        }

        return ExcelType.XLSX;
    }

    /// <summary>
    /// 管理端设置指定用户的双因素认证。启用前提与自助侧同源（TwoFactorGuard 单一出处）。
    /// ABP 动态 API 路由：POST /api/app/identity-user-admin/{id}/set-two-factor-enabled
    /// </summary>
    [OperationLog("身份管理", "设置双因素认证", BizNo = "{{id}}", Success = "设置了用户 {{user(id)}} 的双因素认证")]
    public virtual async Task SetTwoFactorEnabledAsync(Guid id, SetUserTwoFactorEnabledDto input)
    {
        var user = await _userManager.GetByIdAsync(id);

        if (input.Enabled)
        {
            TwoFactorGuard.EnsureCanEnable(user);
        }

        ThrowIfFailed(await _userManager.SetTwoFactorEnabledAsync(user, input.Enabled));
    }

    /// <summary>
    /// 布尔文本列解析（模板下拉「是/否」，兼容旧模板与导出端字典文本）：
    /// 是/否、启用/停用、true/false、1/0（大小写不敏感）；空值取 <paramref name="defaultValue"/>。
    /// </summary>
    protected virtual bool ParseImportBoolean(string? value, string columnName, int rowNumber, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        // ToLowerInvariant：MiniExcel 读布尔单元格给的是 "True"，手填的 "TRUE"/"true" 也要接受。
        // 词汇表单一出处见 UserImportBooleanTexts（与模板下拉/导出渲染共用）。
        var normalized = value.Trim().ToLowerInvariant();
        if (UserImportBooleanTexts.TrueTexts.Contains(normalized))
        {
            return true;
        }

        if (UserImportBooleanTexts.FalseTexts.Contains(normalized))
        {
            return false;
        }

        throw new UserFriendlyException(L["UserImport:InvalidBoolean", rowNumber, columnName, value.Trim()]);
    }

    /// <summary>
    /// 校验必填字段与用户名/邮箱唯一性。
    /// </summary>
    protected virtual async Task ValidateRowAsync(UserImportRowDto row, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(row.UserName))
        {
            throw new UserFriendlyException(L["UserImport:UserNameRequired", rowNumber]);
        }

        if (string.IsNullOrWhiteSpace(row.Email))
        {
            throw new UserFriendlyException(L["UserImport:EmailRequired", rowNumber]);
        }

        var existingUser = await _userManager.FindByNameAsync(row.UserName);
        if (existingUser != null)
        {
            throw new UserFriendlyException(L["UserImport:DuplicatedUserName", rowNumber, row.UserName]);
        }

        existingUser = await _userManager.FindByEmailAsync(row.Email);
        if (existingUser != null)
        {
            throw new UserFriendlyException(L["UserImport:DuplicatedEmail", rowNumber, row.Email]);
        }
    }

    /// <summary>
    /// 构造并创建用户（外部用户免密，本地用户必须提供密码）。
    /// </summary>
    protected virtual async Task<Volo.Abp.Identity.IdentityUser> CreateUserAsync(UserImportRowDto row, int rowNumber)
    {
        var isActive = ParseImportBoolean(row.IsActive, UserImportColumnNames.IsActive, rowNumber, defaultValue: true);
        var isExternal = ParseImportBoolean(row.IsExternal, UserImportColumnNames.IsExternal, rowNumber, defaultValue: false);

        var user = new Volo.Abp.Identity.IdentityUser(
            GuidGenerator.Create(),
            row.UserName,
            row.Email,
            CurrentTenant.Id)
        {
            Name = row.Name,
            Surname = row.Surname
        };

        user.SetIsActive(isActive);

        if (!string.IsNullOrWhiteSpace(row.PhoneNumber))
        {
            user.SetPhoneNumber(row.PhoneNumber, false);
        }

        if (isExternal)
        {
            // 外部用户不设密码
            user.IsExternal = true;
            ThrowIfFailed(await _userManager.CreateAsync(user));
        }
        else
        {
            // 本地用户需要密码
            if (string.IsNullOrWhiteSpace(row.Password))
            {
                throw new UserFriendlyException(L["UserImport:PasswordRequiredForLocalUser", rowNumber]);
            }
            ThrowIfFailed(await _userManager.CreateAsync(user, row.Password));
        }

        return user;
    }

    /// <summary>
    /// 解析并分配角色。角色名合法性对照 <paramref name="knownRoleNames"/>（ImportAsync 文件级解析一次传入），
    /// 替代逐行全表查询。
    /// </summary>
    protected virtual async Task AssignRolesAsync(
        Volo.Abp.Identity.IdentityUser user,
        UserImportRowDto row,
        int rowNumber,
        IReadOnlySet<string> knownRoleNames)
    {
        if (string.IsNullOrWhiteSpace(row.RoleNames))
        {
            return;
        }

        var roleNames = row.RoleNames.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .ToArray();

        if (roleNames.Length == 0)
        {
            return;
        }

        var missing = roleNames.Where(r => !knownRoleNames.Contains(r)).Distinct().ToList();
        if (missing.Count > 0)
        {
            throw new UserFriendlyException(L["UserImport:RoleNotFound", rowNumber, string.Join(";", missing)]);
        }

        ThrowIfFailed(await _userManager.SetRolesAsync(user, roleNames));
    }

    /// <summary>
    /// 保存失败明细到 BLOB。
    /// errorRows 与 errors 由 ImportAsync 同步收集（同一下标对应同一行），直接按下标对齐取原始行号，
    /// 不再按 UserName 反查——重复用户名多行失败时反查会把所有行都算到第一个匹配头上。
    /// </summary>
    protected virtual async Task<Guid> SaveFailureReportAsync(
        List<UserImportRowDto> errorRows,
        List<UserImportRowErrorDto> errors)
    {
        var reportRows = errorRows.Select((row, i) => new
        {
            行号 = errors[i].RowNumber,
            用户名 = SanitizeSpreadsheetText(row.UserName),
            名 = SanitizeSpreadsheetText(row.Name),
            姓 = SanitizeSpreadsheetText(row.Surname),
            邮箱 = SanitizeSpreadsheetText(row.Email),
            手机号 = SanitizeSpreadsheetText(row.PhoneNumber),
            角色 = SanitizeSpreadsheetText(row.RoleNames),
            组织单元 = SanitizeSpreadsheetText(row.OrganizationUnitCodes),
            是否启用 = row.IsActive,
            是否外部用户 = row.IsExternal,
            失败原因 = SanitizeSpreadsheetText(errors[i].ErrorMessage)
        });

        using var stream = new MemoryStream();
        MiniExcel.SaveAs(stream, reportRows);
        var bytes = stream.ToArray();

        var fileId = GuidGenerator.Create();
        var blobName = fileId.ToString();
        await _blobContainer.SaveAsync(blobName, bytes);

        var fileName = $"user-import-failures-{Clock.Now:yyyyMMdd-HHmmss}.xlsx";
        var excelFile = new UserExcelFile(
            fileId,
            fileName,
            CurrentTenant.Id,
            CurrentUser.GetId());

        await _excelFileRepository.InsertAsync(excelFile);

        return fileId;
    }

    /// <summary>
    /// 电子表格公式注入加固（OWASP CSV Injection 的 xlsx 对应物，纵深防御）：
    /// 失败明细的文本单元格全部来自导入文件原文（攻击者可控），当前 MiniExcel 写
    /// inline string 不可利用，但一旦输出格式改为 CSV 或写入方式变更即成公式注入面——
    /// 对以 = + - @ Tab CR 开头的值加 ' 前缀，让危险字符永远以字面量呈现。
    /// </summary>
    protected virtual string SanitizeSpreadsheetText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    /// <summary>
    /// 将异常转换为用户友好的错误消息（非用户友好异常不暴露原始消息）。
    /// </summary>
    protected virtual string GetFriendlyErrorMessage(Exception ex)
    {
        if (ex is UserFriendlyException userFriendlyEx)
        {
            return userFriendlyEx.Message;
        }

        return L["UserImport:Failed"];
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new UserFriendlyException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
