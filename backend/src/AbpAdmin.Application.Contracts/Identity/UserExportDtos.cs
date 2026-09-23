using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using EasyAbp.Abp.DataDictionary;
using MiniExcelLibs.Attributes;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户导出结果。
/// 同步导出时直接返回文件流（走 Controller），异步导出时返回 Queued 状态。
/// </summary>
public class UserExportResultDto
{
    public bool IsQueued { get; set; }

    public string? Message { get; set; }

    public static UserExportResultDto Queued() => new()
    {
        IsQueued = true,
        Message = "导出任务已排队，完成后将通过邮件通知您。"
    };
}

/// <summary>
/// 用户导入结果。
/// </summary>
public class UserImportResultDto
{
    public int TotalCount { get; set; }

    public int SucceededCount { get; set; }

    public int FailedCount { get; set; }

    /// <summary>
    /// 失败明细文件的 BLOB 记录 id，无失败时为 null。
    /// </summary>
    public Guid? FailureReportId { get; set; }

    /// <summary>
    /// 只返回前 50 条错误，完整明细走下载。
    /// </summary>
    public List<UserImportRowErrorDto> Errors { get; set; } = new();
}

public class UserImportRowErrorDto
{
    public int RowNumber { get; set; }

    public string? UserName { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 用户导入行 DTO。列名与导入模板表头一致，使用同一份常量定义（[ExcelColumnName] 引用
/// UserImportColumnNames，MiniExcel 按特性映射中文表头；只靠属性名会导致模板文件解析成全 null 行）。
/// </summary>
public class UserImportRowDto
{
    [ExcelColumnName(UserImportColumnNames.UserName)]
    public string? UserName { get; set; }

    [ExcelColumnName(UserImportColumnNames.Name)]
    public string? Name { get; set; }

    [ExcelColumnName(UserImportColumnNames.Surname)]
    public string? Surname { get; set; }

    [ExcelColumnName(UserImportColumnNames.Email)]
    public string? Email { get; set; }

    [ExcelColumnName(UserImportColumnNames.PhoneNumber)]
    public string? PhoneNumber { get; set; }

    [ExcelColumnName(UserImportColumnNames.Password)]
    public string? Password { get; set; }

    [ExcelColumnName(UserImportColumnNames.RoleNames)]
    public string? RoleNames { get; set; }

    [ExcelColumnName(UserImportColumnNames.OrganizationUnitCodes)]
    public string? OrganizationUnitCodes { get; set; }

    /// <summary>
    /// 布尔列以文本承载（与模板下拉「是/否」、导出端字典渲染对称）：
    /// 接受 是/否、启用/停用、true/false、1/0（大小写不敏感），空值取默认（启用）。
    /// 转换在 IdentityUserAdminAppService.ParseImportBoolean 校验并出行级错误。
    /// </summary>
    [ExcelColumnName(UserImportColumnNames.IsActive)]
    public string? IsActive { get; set; }

    /// <summary>同 <see cref="IsActive"/>；空值默认否（本地用户）。</summary>
    [ExcelColumnName(UserImportColumnNames.IsExternal)]
    public string? IsExternal { get; set; }
}

/// <summary>
/// 用户导入模板构建器（对标 ruoyi @ExcelColumnSelect 的下拉模板）。
/// 接口在 Contracts、实现在 Application（与 IUserExcelBuilder 同款分层）。
/// </summary>
public interface IUserImportTemplateBuilder
{
    /// <summary>
    /// 生成导入模板 xlsx 字节：表头 + 示例行 + 数据验证下拉
    /// （布尔列硬校验「是/否」；角色/组织单元咨询式下拉，数据源见隐藏「参考数据」sheet）。
    /// </summary>
    Task<byte[]> BuildAsync();
}

/// <summary>
/// 布尔列文本词汇单一出处：模板下拉、导入解析、导出渲染三处共用，
/// 改词只改这里（否则导出产物会不再被导入解析接受）。
/// </summary>
public static class UserImportBooleanTexts
{
    /// <summary>模板下拉提供的取值（也是导出回退词）。</summary>
    public const string Yes = "是";
    public const string No = "否";

    /// <summary>导入端额外接受的同义词（对称导出端 EnabledStatus 字典文本与旧模板布尔单元格）。</summary>
    public static readonly string[] TrueTexts = [Yes, "启用", "true", "1"];
    public static readonly string[] FalseTexts = [No, "停用", "false", "0"];
}

/// <summary>
/// 用户导入模板列名常量。模板下载与导入解析共用此常量，避免不同步。
/// </summary>
public static class UserImportColumnNames
{
    public const string UserName = "用户名";
    public const string Name = "名";
    public const string Surname = "姓";
    public const string Email = "邮箱";
    public const string PhoneNumber = "手机号";
    public const string Password = "密码";
    public const string RoleNames = "角色";
    public const string OrganizationUnitCodes = "组织单元";
    public const string IsActive = "是否启用";
    public const string IsExternal = "是否外部用户";
}

/// <summary>
/// 用户导出列名常量（与 <see cref="UserImportColumnNames"/> 对称，见重构报告问题 2）。
/// 导出行用 Dictionary 承载、以本常量为键，列名变更只改一处。
/// </summary>
public static class UserExportColumnNames
{
    public const string UserName = "用户名";
    public const string Name = "名";
    public const string Surname = "姓";
    public const string Email = "邮箱";
    public const string EmailConfirmed = "邮箱已确认";
    public const string PhoneNumber = "手机号";
    public const string PhoneNumberConfirmed = "手机号已确认";
    public const string IsActive = "是否启用";
    public const string LockoutEnd = "锁定截止时间";
    public const string RoleNames = "角色";
    public const string OrganizationUnits = "组织单元";
    public const string CreationTime = "创建时间";
    public const string LastPasswordChangeTime = "最后密码修改时间";
}

/// <summary>
/// 当前账户状态。
/// </summary>
public class AccountStatusDto
{
    public bool ShouldChangePassword { get; set; }

    public string? Reason { get; set; }
}

/// <summary>
/// 用户导出行的字典渲染载体（T3.4 第 9 步）。
/// 导出场景没有前端参与，必须在服务端把编码渲染成显示文本，所以这里用
/// [DictionaryCodeField]/[DictionaryRenderField] 声明式标注，由 UserExcelBuilder 调 Renderer。
/// 注意取舍：普通列表查询接口不要加这种 XxxText 字段（白增数据量），渲染只用于导出/模板路径。
/// </summary>
public class UserExportRenderDto
{
    /// <summary>EnabledStatus 字典编码：Enabled / Disabled。</summary>
    [DictionaryCodeField(AbpAdminDictionaryCodes.EnabledStatus)]
    public string? IsActiveCode { get; set; }

    /// <summary>渲染结果：EnabledStatus 字典的显示文本（启用/停用）。</summary>
    [DictionaryRenderField(AbpAdminDictionaryCodes.EnabledStatus)]
    public string? IsActiveText { get; set; }
}
