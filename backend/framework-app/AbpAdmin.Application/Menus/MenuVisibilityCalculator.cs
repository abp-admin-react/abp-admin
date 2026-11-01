using System;
using System.Collections.Generic;
using System.Linq;

namespace AbpAdmin.Menus;

/// <summary>
/// 用户可见菜单计算器（纯函数，从 MyMenuAppService.GetAsync 的 148 行长方法中提取）。
/// 输入：全量菜单 + 用户角色 + 是否 admin + 已授予权限集 + 全量角色勾选记录；
/// 输出：组装后的 <see cref="MyMenuItemDto"/> 树。
/// 与 <see cref="MenuTreeBuilder"/>（Application 层）互为补充：管理端全量树用后者，
/// 用户端可见性过滤树用本类——两套遍历语义各集中一处，避免漂移。
/// <para>可见性判定（混合授权）：IsEnabled 且非 IsHide，且（未绑定权限或权限已授予），
/// 且（admin 或 未受角色勾选控制 或 用户角色命中勾选）；停用节点整棵子树剔除；
/// 隐藏节点自身不出现、子节点上浮到最近的可见祖先；目录子级全灭则整组折叠。</para>
/// <para>三个内部方法（ComputeVisibleIds / BuildChildrenMap / Assemble）均为可单测的纯函数。</para>
/// </summary>
public static class MenuVisibilityCalculator
{
    /// <summary>根级挂载键（避免可空 Guid 的字典键）。</summary>
    private static readonly Guid RootKey = Guid.Empty;

    /// <summary>
    /// 计算并组装当前用户可见的菜单树。
    /// roleGrants 传「该租户全部 ProviderName=R 的勾选记录」（与原实现一致：存在任一勾选记录的节点视为受控）。
    /// </summary>
    public static List<MyMenuItemDto> Build(
        IReadOnlyCollection<Menu> menus,
        IReadOnlyCollection<string> roleNames,
        bool isAdmin,
        ISet<string> grantedPermissions,
        IReadOnlyCollection<MenuGrant> roleGrants)
    {
        var byId = menus.ToDictionary(x => x.Id);

        var grantControlledMenuIds = roleGrants.Select(x => x.MenuId).ToHashSet();
        var userGrantedMenuIds = roleGrants
            .Where(x => roleNames.Contains(x.ProviderKey))
            .Select(x => x.MenuId)
            .ToHashSet();

        bool ConstraintPassed(Menu m) =>
            (m.PermissionName == null || grantedPermissions.Contains(m.PermissionName)) &&
            (isAdmin || !grantControlledMenuIds.Contains(m.Id) || userGrantedMenuIds.Contains(m.Id));

        // 1) 可见集合：自身启用 + 祖先链完整且全启用 + 约束通过（断链视作不可见，保守）
        var visibleIds = ComputeVisibleIds(menus, byId, ConstraintPassed);

        // 2) 输出节点 = 可见且未隐藏；隐藏/被过滤的祖先视作透明，子节点上浮
        var childrenByParentId = BuildChildrenMap(menus, byId, visibleIds);

        // 3) 自顶向下组装并折叠空目录
        return Assemble(
            childrenByParentId.TryGetValue(RootKey, out var rootMenus) ? rootMenus : new List<Menu>(),
            childrenByParentId);
    }

    /// <summary>
    /// 可见节点 Id 集：自身启用 + 祖先链完整且全部启用 + 混合授权约束通过。
    /// 祖先链断链（ParentId 指向不存在的节点）时该节点不可见。
    /// </summary>
    internal static HashSet<Guid> ComputeVisibleIds(
        IReadOnlyCollection<Menu> menus,
        IReadOnlyDictionary<Guid, Menu> byId,
        Func<Menu, bool> constraintPassed)
    {
        var visibleIds = new HashSet<Guid>();
        foreach (var menu in menus.Where(x => x.IsEnabled))
        {
            var chain = MenuTreeWalker.AncestorChainOf(byId, menu.Id);
            if (chain != null && chain.All(a => a.IsEnabled) && constraintPassed(menu))
            {
                visibleIds.Add(menu.Id);
            }
        }

        return visibleIds;
    }

    /// <summary>
    /// 按挂载父分组：每个「可见且未隐藏」的节点挂到最近的可见且未隐藏的祖先下，
    /// 中间的隐藏/被过滤祖先视作透明（子节点上浮），找不到则挂根级。
    /// </summary>
    internal static Dictionary<Guid, List<Menu>> BuildChildrenMap(
        IReadOnlyCollection<Menu> menus,
        IReadOnlyDictionary<Guid, Menu> byId,
        IReadOnlyCollection<Guid> visibleIds)
    {
        var childrenByParentId = new Dictionary<Guid, List<Menu>>();
        foreach (var menu in menus)
        {
            if (!visibleIds.Contains(menu.Id) || menu.IsHide)
            {
                continue;
            }

            var parentKey = ResolveAttachParent(byId, visibleIds, menu) ?? RootKey;
            if (!childrenByParentId.TryGetValue(parentKey, out var list))
            {
                childrenByParentId[parentKey] = list = new List<Menu>();
            }

            list.Add(menu);
        }

        return childrenByParentId;
    }

    /// <summary>组装 DTO 树：OrderNo 升序 + Title 序数；目录节点的子级全灭则整组折叠。</summary>
    internal static List<MyMenuItemDto> Assemble(
        List<Menu> items,
        IReadOnlyDictionary<Guid, List<Menu>> childrenByParentId)
    {
        var result = new List<MyMenuItemDto>();
        foreach (var menu in items.OrderBy(x => x.OrderNo).ThenBy(x => x.Title, StringComparer.Ordinal))
        {
            var children = childrenByParentId.TryGetValue(menu.Id, out var kids)
                ? Assemble(kids, childrenByParentId)
                : new List<MyMenuItemDto>();
            if (menu.Type == MenuTypeEnum.Catalog && children.Count == 0)
            {
                continue;
            }

            result.Add(new MyMenuItemDto
            {
                Title = menu.Title,
                Name = menu.Name,
                Path = menu.Path,
                Icon = menu.Icon,
                Children = children
            });
        }

        return result;
    }

    /// <summary>沿祖先链向上找最近的「可见且未隐藏」祖先作为挂载父；断链或找不到返回 null（挂根级）。</summary>
    private static Guid? ResolveAttachParent(
        IReadOnlyDictionary<Guid, Menu> byId,
        IReadOnlyCollection<Guid> visibleIds,
        Menu menu)
    {
        var chain = MenuTreeWalker.AncestorChainOf(byId, menu.Id);
        if (chain == null)
        {
            return null;
        }

        foreach (var ancestor in chain)
        {
            if (visibleIds.Contains(ancestor.Id) && !ancestor.IsHide)
            {
                return ancestor.Id;
            }
        }

        return null;
    }
}
