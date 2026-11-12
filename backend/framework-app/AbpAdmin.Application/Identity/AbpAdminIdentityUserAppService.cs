using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AbpAdmin.Identity;

/// <summary>
/// 替换 ABP <see cref="IdentityUserAppService"/> 的角色过滤：只认直接成员（AbpUserRoles）。
/// 提交里仅经部门持有的角色名先丢掉，不把「勾选部门角色」当成直接授予。
/// <c>SetRolesAsync</c> 按有效并集做差集，这些名字本来也不会插入 AbpUserRoles。
/// 替换方式同 <see cref="AbpAdminRoleAppService"/>：ReplaceServices + ExposeServices；RemoteService(false) 避免再暴露一份 /api/app。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IIdentityUserAppService), typeof(IdentityUserAppService))]
[Volo.Abp.RemoteService(false)]
public class AbpAdminIdentityUserAppService : IdentityUserAppService
{
    private readonly IUserDirectRoleNameLookup _directRoleNameLookup;

    public AbpAdminIdentityUserAppService(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        IIdentityRoleRepository roleRepository,
        IOptions<IdentityOptions> identityOptions,
        IPermissionChecker permissionChecker,
        IUserDirectRoleNameLookup directRoleNameLookup)
        : base(userManager, userRepository, roleRepository, identityOptions, permissionChecker)
    {
        _directRoleNameLookup = directRoleNameLookup;
    }

    /// <summary>
    /// 专用改角色端点与表单 ManageRoles 门对齐；原生仅要求 Users.Update，UI 隐藏后仍可被直调。
    /// 资料 Create/Update 里带 roleNames 时，基类 <c>UpdateUserByInput</c> 仍先查 ManageRoles 再进本过滤。
    /// </summary>
    [Authorize(IdentityPermissions.Users.ManageRoles)]
    public override Task UpdateRolesAsync(Guid id, IdentityUserUpdateRolesDto input)
    {
        return base.UpdateRolesAsync(id, input);
    }

    /// <summary>
    /// 丢掉目标仅经组织单元持有的角色名（含 admin 分支），再按操作者可授集合过滤。
    /// ABP <c>SetRolesAsync</c> 用有效并集做差集，部门角色名再提交也不会插入 AbpUserRoles；
    /// 这里先丢掉，是为了不把「勾选部门角色」当成一次直接授予。
    /// 非 admin 另保留「目标已有、操作者没有」的直接角色；输入只留操作者有效角色内可授项。
    /// </summary>
    /// <param name="user">被改角色的用户。</param>
    /// <param name="inputRoleNames">客户端提交的角色名；null 当空。</param>
    /// <returns>交给 <c>SetRolesAsync</c> 的直接角色名。</returns>
    protected override async Task<string[]> FilterRolesByCurrentUserAsync(IdentityUser user, string[] inputRoleNames)
    {
        var direct = (await _directRoleNameLookup.GetAsync(user.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effective = (await UserManager.GetRolesAsync(user))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ouOnly = effective.Except(direct, StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 回显有效角色不得把 OU 派生名写入 AbpUserRoles；显式新授（目标当前没有的角色）不受影响。
        var submitted = (inputRoleNames ?? Array.Empty<string>())
            .Where(name => !ouOnly.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (await HasAdminRoleAsync())
        {
            return submitted;
        }

        var operatorUser = await UserManager.GetByIdAsync(CurrentUser.GetId());
        // 操作者侧仍用有效角色：经 OU 持有的角色仍可授予尚未经 OU 获得该角色的他人。
        var operatorRoles = (await UserManager.GetRolesAsync(operatorUser))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var keepUnmanageable = direct.Except(operatorRoles, StringComparer.OrdinalIgnoreCase);
        var allowedInput = submitted.Intersect(operatorRoles, StringComparer.OrdinalIgnoreCase);
        return keepUnmanageable
            .Concat(allowedInput)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
