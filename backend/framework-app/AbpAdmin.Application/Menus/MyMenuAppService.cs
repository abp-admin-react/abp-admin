using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Menus;

/// <summary>
/// 当前登录用户的动态菜单（GET /api/app/my-menu）。仅需登录。
/// 可见性算法收敛在 <see cref="MenuVisibilityCalculator"/>（纯函数，可单测）：
///   IsEnabled 且非 IsHide，且（未绑定权限 或 ABP 权限已授予）。
/// 停用节点整棵子树剔除；隐藏（IsHide）节点自身不出现，子节点上浮到最近的可见祖先；
/// 目录节点子级全灭则整组折叠。
/// </summary>
[Authorize]
public class MyMenuAppService : AbpAdminAppService, IMyMenuAppService
{
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly MenuManager _menuManager;
    private readonly IPermissionChecker _permissionChecker;

    public MyMenuAppService(
        IRepository<Menu, Guid> menuRepository,
        MenuManager menuManager,
        IPermissionChecker permissionChecker)
    {
        _menuRepository = menuRepository;
        _menuManager = menuManager;
        _permissionChecker = permissionChecker;
    }

    public virtual async Task<ListResultDto<MyMenuItemDto>> GetAsync()
    {
        // 租户首次访问时按套餐懒拷贝。宿主读取空菜单时直接返回，保留管理员配置。
        if (CurrentTenant.Id != null)
        {
            await _menuManager.EnsureTenantMenusAsync(CurrentTenant.Id.Value);
        }

        var menus = await _menuRepository.GetListAsync();
        if (CurrentTenant.Id != null)
        {
            // 租户菜单快照可能仍含宿主专属目录。移除该目录后，断链的子树会一起不可见。
            menus.RemoveAll(menu => MenuTemplateDefinition.IsHostOnlyPath(menu.Path));
        }
        if (menus.Count == 0)
        {
            return new ListResultDto<MyMenuItemDto>(new List<MyMenuItemDto>());
        }

        var grantedPermissions = await GetGrantedPermissionsAsync(menus);
        var roots = MenuVisibilityCalculator.Build(menus, grantedPermissions);
        return new ListResultDto<MyMenuItemDto>(roots);
    }

    /// <summary>去重后批量检查菜单绑定的 ABP 权限，减少逐项调用权限提供器。</summary>
    private async Task<HashSet<string>> GetGrantedPermissionsAsync(IReadOnlyCollection<Menu> menus)
    {
        var permissionNames = menus
            .SelectMany(menu => menu.PermissionName == null
                ? menu.GetAnyPermissionNames()
                : menu.GetAnyPermissionNames().Append(menu.PermissionName))
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
