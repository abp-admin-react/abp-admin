using AbpAdmin.DataDictionaries;
using EasyAbp.Abp.DataDictionary;

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
/// 导出布尔列用的「是/否」。
/// </summary>
public static class UserExportBooleanTexts
{
    public const string Yes = "是";
    public const string No = "否";
}

/// <summary>
/// 用户导出列名常量（导出列名）。
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
/// 用户导出行的字典渲染载体。
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
