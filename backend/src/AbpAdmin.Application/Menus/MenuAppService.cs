using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Menus;

/// <summary>
/// 菜单管理（动态菜单）。Host 上下文维护全局模板；租户上下文维护本租户的树（数据过滤器自动隔离）。
/// 菜单可见性为混合授权：可选绑定 ABP 权限 + 可选分配角色，两者都配置则须同时满足。
/// </summary>
[Authorize(AbpAdminPermissions.Menus.Default)]
public class MenuAppService : AbpAdminAppService, IMenuAppService
{
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IStringLocalizerFactory _stringLocalizerFactory;

    public MenuAppService(
        IRepository<Menu, Guid> menuRepository,
        IRepository<MenuGrant, Guid> menuGrantRepository,
        IIdentityRoleRepository roleRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        IStringLocalizerFactory stringLocalizerFactory)
    {
        _menuRepository = menuRepository;
        _menuGrantRepository = menuGrantRepository;
        _roleRepository = roleRepository;
        _permissionDefinitionManager = permissionDefinitionManager;
        _stringLocalizerFactory = stringLocalizerFactory;
    }

    public virtual async Task<ListResultDto<MenuTreeDto>> GetTreeAsync()
    {
        var menus = await _menuRepository.GetListAsync();
        var grants = await _menuGrantRepository.GetListAsync(x => x.ProviderName == MenuConsts.RoleProviderName);

        var grantedRolesByMenuId = grants
            .GroupBy(x => x.MenuId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProviderKey).ToList());

        return new ListResultDto<MenuTreeDto>(MenuTreeBuilder.Build(menus, grantedRolesByMenuId));
    }

    public virtual async Task<MenuDto> GetAsync(Guid id)
    {
        var menu = await GetMenuAsync(id);
        return ObjectMapper.Map<Menu, MenuDto>(menu);
    }

    [Authorize(AbpAdminPermissions.Menus.Create)]
    [OperationLog("菜单管理", "创建菜单", BizNo = "{{_ret.id}}", Success = "创建了菜单「{{input.title}}」")]
    public virtual async Task<MenuDto> CreateAsync(MenuCreateDto input)
    {
        await ValidateInputAsync(input.ParentId, input.Type, input.Path, input.PermissionName, excludeId: null);

        var menu = new Menu(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.ParentId,
            input.Type,
            input.Title,
            input.Name,
            input.Path,
            input.Icon,
            input.OrderNo,
            input.IsHide,
            input.IsEnabled,
            input.PermissionName)
        {
            Remark = input.Remark
        };

        await _menuRepository.InsertAsync(menu, autoSave: true);
        return ObjectMapper.Map<Menu, MenuDto>(menu);
    }

    [Authorize(AbpAdminPermissions.Menus.Update)]
    [OperationLog("菜单管理", "更新菜单", BizNo = "{{id}}", Success = "更新了菜单「{{input.title}}」")]
    public virtual async Task<MenuDto> UpdateAsync(Guid id, MenuUpdateDto input)
    {
        var menu = await GetMenuAsync(id);

        // 乐观并发：客户端回传的并发戳与库内当前值不一致 = 别人已改过，拒绝整表覆盖式更新。
        // ABP 的 SaveChanges 会在保存时轮转 IHasConcurrencyStamp 实体的戳值，本比较因此可靠。
        // 不传戳视为旧客户端，跳过校验（兼容存量调用）。
        if (!string.IsNullOrEmpty(input.ConcurrencyStamp)
            && menu.ConcurrencyStamp != input.ConcurrencyStamp)
        {
            throw new AbpDbConcurrencyException($"Menu '{id}' was modified by another user.");
        }

        if (input.ParentId == id)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuParentCycle);
        }

        await ValidateInputAsync(input.ParentId, input.Type, input.Path, input.PermissionName, excludeId: id);
        await EnsureNoParentCycleAsync(id, input.ParentId);

        menu.SetParent(input.ParentId);
        menu.SetType(input.Type);
        menu.SetTitle(input.Title);
        menu.SetName(input.Name);
        menu.SetPath(input.Path);
        menu.SetIcon(input.Icon);
        menu.SetOrderNo(input.OrderNo);
        menu.SetPermissionName(input.PermissionName);
        menu.Remark = input.Remark;
        menu.SetVisible(!input.IsHide);
        menu.SetEnabled(input.IsEnabled);

        await _menuRepository.UpdateAsync(menu, autoSave: true);
        return ObjectMapper.Map<Menu, MenuDto>(menu);
    }

    [Authorize(AbpAdminPermissions.Menus.Delete)]
    [OperationLog("菜单管理", "删除菜单", BizNo = "{{id}}", Success = "删除了菜单「{{menu(id)}}」")]
    public virtual async Task DeleteAsync(Guid id)
    {
        var menu = await GetMenuAsync(id);

        var childCount = await _menuRepository.CountAsync(x => x.ParentId == id);
        if (childCount > 0)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuHasChildren);
        }

        var grants = await _menuGrantRepository.GetListAsync(x => x.MenuId == id);
        foreach (var grant in grants)
        {
            await _menuGrantRepository.DeleteAsync(grant);
        }

        // 硬删除：软删除行仍占用 (TenantId, Path) 唯一索引，重建同路径会撞唯一约束
        await _menuRepository.HardDeleteAsync(menu);
    }

    public virtual async Task<ListResultDto<string>> GetRoleGrantsAsync(Guid menuId)
    {
        await GetMenuAsync(menuId);
        var grants = await _menuGrantRepository.GetListAsync(x => x.MenuId == menuId && x.ProviderName == MenuConsts.RoleProviderName);
        return new ListResultDto<string>(grants.Select(x => x.ProviderKey).ToList());
    }

    [Authorize(AbpAdminPermissions.Menus.AssignRoles)]
    [OperationLog("菜单管理", "调整角色授权", BizNo = "{{menuId}}", Success = "将菜单「{{menu(menuId)}}」的可访问角色调整为：{{input.roleNames}}")]
    public virtual async Task UpdateRoleGrantsAsync(Guid menuId, UpdateMenuGrantsDto input)
    {
        var menu = await GetMenuAsync(menuId);

        // 角色名必须是当前租户真实存在的角色，防止脏数据；
        // 落库用角色的规范 Name（而非客户端原样大小写）——读取侧 MyMenuAppService
        // 用角色 claim 的原始 Name 做区分大小写匹配，原样存入会导致大小写不一致时永远匹配不上
        var canonicalNames = new Dictionary<string, string>();
        foreach (var roleName in input.RoleNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            var role = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
            if (role == null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuRoleNotFound)
                    .WithData("RoleName", roleName);
            }

            canonicalNames[role.Name] = role.Name;
        }

        var existing = await _menuGrantRepository.GetListAsync(x => x.MenuId == menuId && x.ProviderName == MenuConsts.RoleProviderName);
        var existingNames = existing.Select(x => x.ProviderKey).ToHashSet();
        var targetNames = canonicalNames.Values.ToHashSet();

        foreach (var grant in existing.Where(x => !targetNames.Contains(x.ProviderKey)))
        {
            await _menuGrantRepository.DeleteAsync(grant);
        }

        foreach (var roleName in canonicalNames.Values.Where(x => !existingNames.Contains(x)))
        {
            await _menuGrantRepository.InsertAsync(
                new MenuGrant(GuidGenerator.Create(), menuId, menu.TenantId, MenuConsts.RoleProviderName, roleName),
                autoSave: true);
        }
    }

    public virtual async Task<ListResultDto<PermissionOptionDto>> GetPermissionOptionsAsync()
    {
        var definitions = await _permissionDefinitionManager.GetPermissionsAsync();

        var options = new List<PermissionOptionDto>();
        foreach (var definition in definitions)
        {
            // 租户上下文不展示 Host-only 权限，避免绑定了租户永远拿不到的权限
            if (CurrentTenant.Id != null && definition.MultiTenancySide == MultiTenancySides.Host)
            {
                continue;
            }

            options.Add(new PermissionOptionDto
            {
                Name = definition.Name,
                DisplayName = LocalizePermissionName(definition),
                ParentName = definition.Parent?.Name
            });
        }

        return new ListResultDto<PermissionOptionDto>(options);
    }

    private string LocalizePermissionName(PermissionDefinition definition)
    {
        if (definition.DisplayName is not LocalizableString localizable)
        {
            return definition.Name;
        }

        try
        {
            var localized = localizable.Localize(_stringLocalizerFactory);
            return localized != null && !localized.ResourceNotFound ? localized.Value : definition.Name;
        }
        catch
        {
            return definition.Name;
        }
    }

    private async Task<Menu> GetMenuAsync(Guid id)
    {
        var menu = await _menuRepository.FindAsync(id);
        if (menu == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuNotFound).WithData("Id", id);
        }

        return menu;
    }

    private async Task ValidateInputAsync(
        Guid? parentId,
        MenuTypeEnum type,
        string? path,
        string? permissionName,
        Guid? excludeId)
    {
        if (type == MenuTypeEnum.Menu && string.IsNullOrWhiteSpace(path))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuTypeMismatch);
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            // 防"存储型链接注入"：path 会渲染为全租户用户可点击的站内导航项，
            // 必须是站内路由形态——以 / 开头、拒绝协议相对(//evil.com)与绝对地址(https://evil.com)。
            // 前端路由白名单仅是下拉提示，不是控制；服务端格式校验是唯一防线。
            if (!path.StartsWith("/", StringComparison.Ordinal)
                || path.StartsWith("//", StringComparison.Ordinal)
                || path.Contains("://", StringComparison.Ordinal))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuInvalidPath)
                    .WithData("Path", path);
            }

            var duplicate = await _menuRepository.FindAsync(x => x.Path == path && x.Id != excludeId);
            if (duplicate != null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuDuplicatePath)
                    .WithData("Path", path);
            }
        }

        if (parentId != null)
        {
            var parent = await _menuRepository.FindAsync(parentId.Value);
            if (parent == null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuNotFound).WithData("Id", parentId.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(permissionName))
        {
            // 租户侧排除 Host-only 权限：绑定后 my-menu 的权限检查对租户用户恒 false，
            // 该菜单会对整个租户永久不可见（配置自毁）；与 GetPermissionOptions 的过滤口径一致
            var definition = await _permissionDefinitionManager.GetOrNullAsync(permissionName);
            if (definition == null
                || (CurrentTenant.Id != null && definition.MultiTenancySide == MultiTenancySides.Host))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuInvalidPermission)
                    .WithData("PermissionName", permissionName);
            }
        }
    }

    /// <summary>校验把节点挂到新父级不会形成环：沿父链向上不能经过自身。
    /// 深度上限统一引用 <see cref="MenuConsts.MaxTreeDepth"/>（与 MenuTreeWalker 同一出处）。</summary>
    private async Task EnsureNoParentCycleAsync(Guid id, Guid? newParentId)
    {
        var cursor = newParentId;
        var guard = 0;
        while (cursor != null && guard++ < MenuConsts.MaxTreeDepth)
        {
            if (cursor == id)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuParentCycle);
            }

            var node = await _menuRepository.FindAsync(cursor.Value);
            cursor = node?.ParentId;
        }
    }

}
