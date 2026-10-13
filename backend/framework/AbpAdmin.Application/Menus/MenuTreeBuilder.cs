using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.Menus;

namespace AbpAdmin;

/// <summary>
/// 菜单树组装器（Application 层内部共享）。菜单管理页（MenuAppService.GetTreeAsync）与
/// 套餐菜单勾选（TenantPackageAppService.GetMenuSelectionAsync）需要同一套
/// "实体 → MenuTreeDto 树 + OrderNo/Title 排序"逻辑，集中在此避免两份拷贝漂移。
/// </summary>
public static class MenuTreeBuilder
{
    /// <summary>
    /// 平铺菜单实体 → MenuTreeDto 树。悬空 ParentId（父行不存在）的节点上浮为根，
    /// 与 my-menu 的上浮语义一致，管理端不会因脏数据丢节点。
    /// </summary>
    public static List<MenuTreeDto> Build(IEnumerable<Menu> menus, IReadOnlyDictionary<Guid, List<string>>? grantedRolesByMenuId = null)
    {
        grantedRolesByMenuId ??= new Dictionary<Guid, List<string>>();

        var dtoById = menus.ToDictionary(
            x => x.Id,
            x => new MenuTreeDto
            {
                Id = x.Id,
                TenantId = x.TenantId,
                ParentId = x.ParentId,
                Type = x.Type,
                Title = x.Title,
                Name = x.Name,
                Path = x.Path,
                Icon = x.Icon,
                OrderNo = x.OrderNo,
                IsHide = x.IsHide,
                IsEnabled = x.IsEnabled,
                PermissionName = x.PermissionName,
                Remark = x.Remark,
                ConcurrencyStamp = x.ConcurrencyStamp,
                GrantedRoles = grantedRolesByMenuId.TryGetValue(x.Id, out var roles) ? roles : new List<string>()
            });

        var roots = new List<MenuTreeDto>();
        foreach (var dto in dtoById.Values)
        {
            if (dto.ParentId != null && dtoById.TryGetValue(dto.ParentId.Value, out var parent))
            {
                parent.Children.Add(dto);
            }
            else
            {
                roots.Add(dto);
            }
        }

        Sort(roots);
        return roots;
    }

    /// <summary>OrderNo 升序，同序按 Title 序数比较；递归应用子级。</summary>
    public static void Sort(List<MenuTreeDto> nodes)
    {
        nodes.Sort((a, b) =>
            a.OrderNo != b.OrderNo
                ? a.OrderNo.CompareTo(b.OrderNo)
                : string.CompareOrdinal(a.Title, b.Title));
        foreach (var node in nodes)
        {
            Sort(node.Children);
        }
    }
}
