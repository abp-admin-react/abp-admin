using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AbpAdmin.Identity;

public interface IIdentityUserAdminAppService : IApplicationService
{
    Task LockAsync(Guid id);

    Task UnlockAsync(Guid id);

    /// <summary>
    /// 异步导出用户。转后台作业，完成后发邮件通知。
    /// 同步导出请走 Controller 的 GET /api/app/identity-user-admin/export。
    /// </summary>
    Task<UserExportResultDto> EnqueueExportAsync(string? filter);

    /// <summary>
    /// 导入用户。逐行校验，部分成功语义。
    /// </summary>
    Task<UserImportResultDto> ImportAsync(IRemoteStreamContent file);

    /// <summary>
    /// 批量获取用户双因素认证状态（Volo 开源用户列表契约不含 twoFactorEnabled，列表页据此补齐）。
    /// </summary>
    Task<List<UserTwoFactorStatusDto>> GetTwoFactorStatusesAsync(GetUserTwoFactorStatusesInput input);

    /// <summary>
    /// 获取当前登录账户状态（是否需要强制改密）。
    /// </summary>
    Task<AccountStatusDto> GetCurrentAccountStatusAsync();

    /// <summary>
    /// 要求指定用户下次登录时修改密码。
    /// </summary>
    Task RequireChangePasswordOnNextLoginAsync(Guid id);

    /// <summary>
    /// 管理端按用户启用/禁用双因素认证（对标 ABP Identity Pro 用户页的 2FA 开关）。
    /// </summary>
    Task SetTwoFactorEnabledAsync(Guid id, SetUserTwoFactorEnabledDto input);
}

/// <summary>
/// 管理端设置用户双因素认证入参。
/// </summary>
public class SetUserTwoFactorEnabledDto
{
    public bool Enabled { get; set; }
}

/// <summary>
/// 批量 2FA 状态查询入参（GET 查询串重复键绑定）。
/// </summary>
public class GetUserTwoFactorStatusesInput
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>
/// 用户双因素认证状态。
/// </summary>
public class UserTwoFactorStatusDto
{
    public Guid UserId { get; set; }

    public bool TwoFactorEnabled { get; set; }
}
