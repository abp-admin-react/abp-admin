using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Roles;

namespace AbpAdmin.Menus;

/// <summary>
/// 当前登录用户的动态菜单（GET /api/app/my-menu）。仅需登录。
/// 可见性算法收敛在 <see cref="MenuVisibilityCalculator"/>（纯函数，可单测）：
///   IsEnabled 且非 IsHide，且（未绑定权限 或 权限已授予），且（admin 或 未受角色勾选控制 或 用户角色命中勾选）
/// 停用节点整棵子树剔除；隐藏（IsHide）节点自身不出现，子节点上浮到最近的可见祖先；
/// 目录节点子级全灭则整组折叠。
/// </summary>
[Authorize]
public class MyMenuAppService : AbpAdminAppService, IMyMenuAppService
{
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly MenuManager _menuManager;
    private readonly IPermissionChecker _permissionChecker;

    public MyMenuAppService(
        IRepository<Menu, Guid> menuRepository,
        IRepository<MenuGrant, Guid> menuGrantRepository,
        MenuManager menuManager,
        IPermissionChecker permissionChecker)
    {
        _menuRepository = menuRepository;
        _menuGrantRepository = menuGrantRepository;
        _menuManager = menuManager;
        _permissionChecker = permissionChecker;
    }

    public virtual async Task<ListResultDto<MyMenuItemDto>> GetAsync()
    {
        // 租户首次访问：懒拷贝全局模板（租户已配套餐时由 MenuManager 按套餐过滤）。
        // Host 侧不再做"空表即补种"兜底：管理员删光 Host 菜单是合法配置，
        // 自动复活模板会静默回滚删除操作；播种是 DbMigrator/MenuDataSeedContributor 的职责。
        if (CurrentTenant.Id != null)
        {
            await _menuManager.EnsureTenantMenusAsync(CurrentTenant.Id.Value);
        }

        var menus = await _menuRepository.GetListAsync();
        if (menus.Count == 0)
        {
            return new ListResultDto<MyMenuItemDto>(new List<MyMenuItemDto>());
        }

        var roleNames = CurrentUser.Roles.ToList();
        var isAdmin = roleNames.Contains(AbpRoleConsts.AdminRoleName);
        var grantedPermissions = await GetGrantedPermissionsAsync(menus);

        // 角色勾选：存在任一勾选记录的节点视为受控，需用户角色命中（admin 豁免）
        var allGrants = await _menuGrantRepository.GetListAsync(x => x.ProviderName == MenuConsts.RoleProviderName);

        var roots = MenuVisibilityCalculator.Build(menus, roleNames, isAdmin, grantedPermissions, allGrants);
        return new ListResultDto<MyMenuItemDto>(roots);
    }

    /// <summary>
    /// 权限检查（批量）：绑定权限的节点要求权限已授予（admin 天然全授）。
    /// 逐名 IsGrantedAsync 会遍历全部 ValueProvider（约 27 个权限 × 角色+1 次缓存读取），
    /// 批量重载一次往返拿全量结果。
    /// </summary>
    private async Task<HashSet<string>> GetGrantedPermissionsAsync(IReadOnlyCollection<Menu> menus)
    {
        var permissionNames = menus
            .Where(x => !string.IsNullOrWhiteSpace(x.PermissionName))
            .Select(x => x.PermissionName!)
            .Distinct()
            .ToArray();
        if (permissionNames.Length == 0)
        {
            return new HashSet<string>();
        }

        var batchResult = await _permissionChecker.IsGrantedAsync(permissionNames);
        return batchResult.Result
            .Where(x => x.Value == PermissionGrantResult.Granted)
            .Select(x => x.Key)
            .ToHashSet();
    }
}
