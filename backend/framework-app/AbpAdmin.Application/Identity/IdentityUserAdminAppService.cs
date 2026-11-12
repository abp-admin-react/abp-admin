using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Permissions;
using AbpAdmin.OperationLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AbpAdmin.Identity;

[Authorize(IdentityPermissions.Users.Update)]
public class IdentityUserAdminAppService : AbpAdminAppService, IIdentityUserAdminAppService
{
    /// <summary>批量 2FA 状态查询的 id 数上限（对齐前端最大页大小的量级，防被当全表探测接口）。</summary>
    private const int TwoFactorStatusBatchLimit = 200;

    private readonly IdentityUserManager _userManager;
    private readonly IRepository<Volo.Abp.Identity.IdentityUser, Guid> _userRepository;
    private readonly IUserDirectRoleNameLookup _directRoleNameLookup;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IPermissionChecker _permissionChecker;

    public IdentityUserAdminAppService(
        IdentityUserManager userManager,
        IRepository<Volo.Abp.Identity.IdentityUser, Guid> userRepository,
        IUserDirectRoleNameLookup directRoleNameLookup,
        IBackgroundJobManager backgroundJobManager,
        IPermissionChecker permissionChecker)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _directRoleNameLookup = directRoleNameLookup;
        _backgroundJobManager = backgroundJobManager;
        _permissionChecker = permissionChecker;
    }

    /// <summary>
    /// 直接角色名，供编辑表单做已选种子。没有这个用户时抛 <see cref="Volo.Abp.Domain.Entities.EntityNotFoundException"/>，
    /// 有用户但没有直接角色时回空列表。该异常没有公开错误码，错误管线投影为 500，响应不带实体信息。
    /// 名单与 Excel、FilterRoles 一样走 <see cref="IUserDirectRoleNameLookup"/>。
    /// </summary>
    [Authorize(IdentityPermissions.Users.ManageRoles)]
    public virtual async Task<ListResultDto<string>> GetDirectRoleNamesAsync(Guid id)
    {
        await _userManager.GetByIdAsync(id);
        var names = await _directRoleNameLookup.GetAsync(id);
        return new ListResultDto<string>(names.ToList());
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
    /// 批量获取用户双因素认证状态。Volo 开源版用户列表契约（IdentityUserDto）不含
    /// twoFactorEnabled 字段，列表页用它页级补齐——缺失时 2FA 列恒显示「否」、开关恒发 true
    /// （function-High）。
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
    // Class-level Users.Update would also apply; any signed-in user (e.g. finance roles) must reach this.
    [AllowAnonymous]
    public virtual async Task<AccountStatusDto> GetCurrentAccountStatusAsync()
    {
        if (!CurrentUser.IsAuthenticated)
        {
            throw new Volo.Abp.Authorization.AbpAuthorizationException();
        }

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

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new UserFriendlyException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
