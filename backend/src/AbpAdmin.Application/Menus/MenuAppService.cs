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
using Volo.Abp.Domain.Entities;
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
    private readonly MenuManager _menuManager;

    public MenuAppService(
        IRepository<Menu, Guid> menuRepository,
        IRepository<MenuGrant, Guid> menuGrantRepository,
        IIdentityRoleRepository roleRepository,
        IPermissionDefinitionManager permissionDefinitionManager,
        IStringLocalizerFactory stringLocalizerFactory,
        MenuManager menuManager)
    {
        _menuRepository = menuRepository;
        _menuGrantRepository = menuGrantRepository;
        _roleRepository = roleRepository;
        _permissionDefinitionManager = permissionDefinitionManager;
        _stringLocalizerFactory = stringLocalizerFactory;
        _menuManager = menuManager;
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

    /// <summary>读取与写入同口径：角色分配属管理操作，仅 AssignRoles 可见
    /// （此前只挂类级 Default，任何能打开菜单管理页的用户都能枚举授权明细）。</summary>
    [Authorize(AbpAdminPermissions.Menus.AssignRoles)]
    public virtual async Task<ListResultDto<string>> GetMenuRoleGrantsAsync(Guid menuId)
    {
        await GetMenuAsync(menuId);
        var grants = await _menuGrantRepository.GetListAsync(x => x.MenuId == menuId && x.ProviderName == MenuConsts.RoleProviderName);
        return new ListResultDto<string>(grants.Select(x => x.ProviderKey).ToList());
    }

    [Authorize(AbpAdminPermissions.Menus.AssignRoles)]
    [OperationLog("菜单管理", "调整角色授权", BizNo = "{{menuId}}", Success = "将菜单「{{menu(menuId)}}」的可访问角色调整为：{{input.roleNames}}")]
    public virtual async Task UpdateMenuRoleGrantsAsync(Guid menuId, UpdateMenuGrantsDto input)
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
                new MenuGrant(GuidGenerator.Create(), menuId, menu.TenantId, MenuConsts.RoleProviderName, roleName));
        }

        await SaveGrantsOrThrowConflictAsync();
    }

    /// <summary>
    /// 授权差集应用后立即落库，并把唯一索引冲突转成明确的业务错误（HTTP 403 语义）。
    /// (MenuId, ProviderName, ProviderKey) 唯一索引把并发双写（角色侧差集 / 菜单侧全量 /
    /// 级联清理共享一张表）从「静默重复行」转成可捕获冲突——冲突即「另一位管理者刚改过
    /// 同一份授权」：fail-closed 拒绝本单（事务整体回滚、无部分落库），提示刷新后以最新
    /// 状态重提，优于裸 500。显式 SaveChanges 是为了在应用层边界内捕获，而非等到
    /// UoW 提交时异常已脱离本方法。
    /// </summary>
    private async Task SaveGrantsOrThrowConflictAsync()
    {
        try
        {
            await UnitOfWorkManager.Current!.SaveChangesAsync();
        }
        // DataException 是 DbUpdateException 的基类——Application 层刻意不引 EF Core
        // （AuditLogAppService.RestoreEntityChangeAsync 同款手法），用 provider 无关
        // 基类型接住存储层失败；唯一键冲突的具体判定交给消息里的约束名（跨 PG/SQLite 稳定）
        catch (System.Data.DataException ex)
            when (ex.InnerException?.Message?.Contains("IX_AppMenuGrants", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuGrantConflict);
        }
    }

    /// <summary>
    /// 角色侧菜单授权视图。菜单页的 UpdateMenuRoleGrantsAsync 是「这张菜单给哪些角色」的单向视角；
    /// 本视图提供反向视角（「这个角色能看哪些菜单」）——管理员配一个受限角色时在一棵树上勾完，
    /// 不必挨张菜单打开授权。IsControlled 标注节点是否受控：未受控=公开（勾不勾都不影响其可见性），
    /// 受控=仅勾选角色可见——前端以徽标区分，避免把混合授权模型误解成纯授权模型。
    /// </summary>
    [Authorize(AbpAdminPermissions.Menus.AssignRoles)]
    public virtual async Task<ListResultDto<RoleMenuGrantItemDto>> GetRoleMenuGrantsAsync(Guid roleId)
    {
        var role = await _roleRepository.FindAsync(roleId)
                   ?? throw new EntityNotFoundException(typeof(Volo.Abp.Identity.IdentityRole), roleId);

        // 与 MyMenuAppService 同口径：租户首次进入授权视图前懒拷贝全局模板，
        // 否则新租户在弹窗里看到空树（无菜单可勾）而非完整模板
        if (CurrentTenant.Id != null)
        {
            await _menuManager.EnsureTenantMenusAsync(CurrentTenant.Id.Value);
        }

        var menus = await _menuRepository.GetListAsync();
        var grants = await _menuGrantRepository.GetListAsync(x => x.ProviderName == MenuConsts.RoleProviderName);

        var grantedMenuIds = grants
            .Where(x => x.ProviderKey == role.Name)
            .Select(x => x.MenuId)
            .ToHashSet();
        var controlledMenuIds = grants.Select(x => x.MenuId).ToHashSet();

        var items = menus
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Title, StringComparer.Ordinal)
            .Select(x => new RoleMenuGrantItemDto
            {
                Id = x.Id,
                ParentId = x.ParentId,
                Type = x.Type,
                Title = x.Title,
                OrderNo = x.OrderNo,
                IsEnabled = x.IsEnabled,
                IsHide = x.IsHide,
                IsGranted = grantedMenuIds.Contains(x.Id),
                IsControlled = controlledMenuIds.Contains(x.Id)
            })
            .ToList();

        return new ListResultDto<RoleMenuGrantItemDto>(items);
    }

    /// <summary>
    /// 以勾选集为该角色的菜单授权全集做差集更新（借鉴 Admin.NET/芋道「角色→菜单树」交互，
    /// 语义适配混合授权模型）：勾上=插入授权，勾掉=删除该角色授权；未受控菜单与其它角色的授权
    /// 不受影响。与 UpdateMenuRoleGrantsAsync 写同一张 MenuGrant 表，读取侧（MyMenuAppService）
    /// 无感。勾选集是逐节点的精确集合（无父子推导）——与菜单可见性的逐节点语义一致。
    /// </summary>
    [Authorize(AbpAdminPermissions.Menus.AssignRoles)]
    [OperationLog("菜单管理", "调整角色菜单权限", BizNo = "{{roleId}}",
        Success = "将角色 {{role(roleId)}} 的菜单授权调整为：{{input.menuIds}}")]
    public virtual async Task UpdateRoleMenuGrantsAsync(Guid roleId, UpdateRoleMenuGrantsDto input)
    {
        var role = await _roleRepository.FindAsync(roleId)
                   ?? throw new EntityNotFoundException(typeof(Volo.Abp.Identity.IdentityRole), roleId);

        // 幽灵菜单拒绝（fail-closed，与 RoleDataScope 的 OU 校验同款）：勾选集里混入
        // 已被删除的菜单 id 时整单拒绝，而不是静默吞掉造成「保存成功但授权缺失」。
        // 取实体而非 Count：新授权行的 TenantId 必须取自菜单实体（MenuGrant 契约是
        // 「租户隔离跟随菜单实体」），与菜单侧写入口同一口径
        var targetMenuIds = input.MenuIds.Distinct().ToList();
        var targetMenus = await _menuRepository.GetListAsync(x => targetMenuIds.Contains(x.Id));
        if (targetMenus.Count != targetMenuIds.Count)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Menus.MenuNotFound)
                .WithData("Count", targetMenuIds.Count - targetMenus.Count);
        }
        var tenantIdByMenuId = targetMenus.ToDictionary(m => m.Id, m => m.TenantId);

        var grants = await _menuGrantRepository.GetListAsync(
            x => x.ProviderName == MenuConsts.RoleProviderName && x.ProviderKey == role.Name);
        var existingMenuIds = grants.Select(x => x.MenuId).ToHashSet();

        foreach (var grant in grants.Where(x => !targetMenuIds.Contains(x.MenuId)))
        {
            await _menuGrantRepository.DeleteAsync(grant);
        }

        foreach (var menuId in targetMenuIds.Where(id => !existingMenuIds.Contains(id)))
        {
            // 不用 autoSave: true：逐行 SaveChanges 会随勾选规模线性放大 DB 往返，
            // 统一在 SaveGrantsOrThrowConflictAsync 一次性落库
            await _menuGrantRepository.InsertAsync(
                new MenuGrant(GuidGenerator.Create(), menuId, tenantIdByMenuId[menuId], MenuConsts.RoleProviderName, role.Name!));
        }

        await SaveGrantsOrThrowConflictAsync();
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
