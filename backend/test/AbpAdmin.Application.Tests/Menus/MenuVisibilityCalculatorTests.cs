using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace AbpAdmin.Menus;

/* MenuVisibilityCalculator（my-menu 可见性算法）的纯函数单测：
 * 覆盖「隐藏上浮 / 停用剔除 / 空目录折叠 / 角色勾选 / admin 豁免 / 权限绑定 / 断链 / 排序」矩阵。
 * 不依赖测试基建（纯静态类 + 内存构造实体），是对 MyMenuAppService.GetAsync 重构抽出的
 * 计算器的行为锚定；集成路径（含懒拷贝、真实权限检查器）由 MenuAppServiceTests 覆盖。
 */
public class MenuVisibilityCalculatorTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Catalog = Guid.NewGuid();
    private static readonly Guid Child = Guid.NewGuid();

    private static Menu NewMenu(
        Guid id,
        Guid? parentId = null,
        MenuTypeEnum type = MenuTypeEnum.Menu,
        string? path = null,
        bool isHide = false,
        bool isEnabled = true,
        string? permissionName = null,
        int orderNo = MenuConsts.DefaultOrderNo)
    {
        var finalPath = path ?? $"/{id:N}";
        return new Menu(Guid.NewGuid(), tenantId: null, parentId, type, finalPath, null, finalPath,
            null, orderNo, isHide, isEnabled, permissionName);
    }

    private static List<MyMenuItemDto> Build(
        List<Menu> menus,
        string[] roleNames,
        bool isAdmin = false,
        string[]? grantedPermissions = null,
        MenuGrant[]? grants = null)
    {
        return MenuVisibilityCalculator.Build(
            menus,
            roleNames,
            isAdmin,
            grantedPermissions?.ToHashSet() ?? new HashSet<string>(),
            grants ?? Array.Empty<MenuGrant>());
    }

    private static List<string> FlattenPaths(List<MyMenuItemDto> items)
    {
        var result = new List<string>();
        void Walk(IEnumerable<MyMenuItemDto> list)
        {
            foreach (var item in list)
            {
                result.Add(item.Path!);
                Walk(item.Children);
            }
        }

        Walk(items);
        return result;
    }

    [Fact]
    public void Public_Node_Visible_Without_Any_Grant()
    {
        var menu = NewMenu(Guid.NewGuid());
        var tree = Build(new List<Menu> { menu }, Array.Empty<string>());

        FlattenPaths(tree).ShouldContain(menu.Path);
    }

    [Fact]
    public void Permission_Bound_Node_Visible_Only_When_Granted()
    {
        var grantedMenu = NewMenu(Guid.NewGuid(), permissionName: "AbpAdmin.Menus");
        var deniedMenu = NewMenu(Guid.NewGuid(), permissionName: "AbpAdmin.Other");

        var tree = Build(
            new List<Menu> { grantedMenu, deniedMenu },
            Array.Empty<string>(),
            grantedPermissions: new[] { "AbpAdmin.Menus" });

        var paths = FlattenPaths(tree);
        paths.ShouldContain(grantedMenu.Path);
        paths.ShouldNotContain(deniedMenu.Path);
    }

    [Fact]
    public void Role_Controlled_Node_Follows_User_Roles()
    {
        var menu = NewMenu(Guid.NewGuid());
        var grant = new MenuGrant(Guid.NewGuid(), menu.Id, null, MenuConsts.RoleProviderName, "operator");

        // 用户没有勾选角色 → 不可见（fail-closed）
        FlattenPaths(Build(new List<Menu> { menu }, new[] { "other-role" }, grants: new[] { grant }))
            .ShouldNotContain(menu.Path);

        // 用户命中勾选角色 → 可见
        FlattenPaths(Build(new List<Menu> { menu }, new[] { "operator" }, grants: new[] { grant }))
            .ShouldContain(menu.Path);
    }

    [Fact]
    public void Admin_Bypasses_Role_Control()
    {
        var menu = NewMenu(Guid.NewGuid());
        var grant = new MenuGrant(Guid.NewGuid(), menu.Id, null, MenuConsts.RoleProviderName, "operator");

        FlattenPaths(Build(new List<Menu> { menu }, new[] { "unrelated" }, isAdmin: true, grants: new[] { grant }))
            .ShouldContain(menu.Path);
    }

    [Fact]
    public void Disabled_Ancestor_Removes_Whole_Subtree()
    {
        var disabledCatalog = NewMenu(Guid.NewGuid(), type: MenuTypeEnum.Catalog, path: "/disabled", isEnabled: false);
        var child = NewMenu(Guid.NewGuid(), parentId: disabledCatalog.Id, path: "/disabled/child");

        var paths = FlattenPaths(Build(new List<Menu> { disabledCatalog, child }, Array.Empty<string>()));
        paths.ShouldNotContain("/disabled");
        paths.ShouldNotContain("/disabled/child");
    }

    [Fact]
    public void Hidden_Node_Drops_Self_And_Children_Bubble_Up()
    {
        var hiddenCatalog = NewMenu(Guid.NewGuid(), type: MenuTypeEnum.Catalog, path: "/hidden", isHide: true);
        var child = NewMenu(Guid.NewGuid(), parentId: hiddenCatalog.Id, path: "/hidden/child");

        var tree = Build(new List<Menu> { hiddenCatalog, child }, Array.Empty<string>());

        // 隐藏目录自身不出现；子页上浮到根级
        var paths = FlattenPaths(tree);
        paths.ShouldNotContain("/hidden");
        paths.ShouldContain("/hidden/child");
        tree.Any(x => x.Path == "/hidden/child").ShouldBeTrue();
    }

    [Fact]
    public void Hidden_Child_Bubbles_Up_To_Nearest_Visible_Ancestor()
    {
        // root(可见) > mid(隐藏) > leaf(可见)：leaf 应上浮挂到 root 下，而不是根级
        var root = NewMenu(Guid.NewGuid(), type: MenuTypeEnum.Catalog, path: "/root");
        var mid = NewMenu(Guid.NewGuid(), parentId: root.Id, type: MenuTypeEnum.Catalog, path: "/root/mid", isHide: true);
        var leaf = NewMenu(Guid.NewGuid(), parentId: mid.Id, path: "/root/mid/leaf");

        var tree = Build(new List<Menu> { root, mid, leaf }, Array.Empty<string>());

        tree.Count.ShouldBe(1);
        tree[0].Path.ShouldBe("/root");
        tree[0].Children.Count.ShouldBe(1);
        tree[0].Children[0].Path.ShouldBe("/root/mid/leaf");
    }

    [Fact]
    public void Empty_Catalog_Folded()
    {
        var catalog = NewMenu(Guid.NewGuid(), type: MenuTypeEnum.Catalog, path: "/empty");
        var menu = NewMenu(Guid.NewGuid(), path: "/sibling");

        var tree = Build(new List<Menu> { catalog, menu }, Array.Empty<string>());

        FlattenPaths(tree).ShouldNotContain("/empty");
        FlattenPaths(tree).ShouldContain("/sibling");
    }

    [Fact]
    public void Catalog_With_Visible_Descendant_Kept()
    {
        // 目录的可见后代藏在更深的已过滤层级下时目录仍保留（不折叠）
        var catalog = NewMenu(Guid.NewGuid(), type: MenuTypeEnum.Catalog, path: "/keep");
        var child = NewMenu(Guid.NewGuid(), parentId: catalog.Id, path: "/keep/child");

        var tree = Build(new List<Menu> { catalog, child }, Array.Empty<string>());

        tree.Count.ShouldBe(1);
        tree[0].Path.ShouldBe("/keep");
        tree[0].Children[0].Path.ShouldBe("/keep/child");
    }

    [Fact]
    public void Broken_Parent_Chain_Hides_Node()
    {
        // ParentId 指向不存在的节点（悬空）→ 该节点不可见（保守语义）
        var orphan = new Menu(Guid.NewGuid(), null, Guid.NewGuid(), MenuTypeEnum.Menu, "孤儿", null, "/orphan");
        var paths = FlattenPaths(Build(new List<Menu> { orphan }, Array.Empty<string>()));
        paths.ShouldNotContain("/orphan");
    }

    [Fact]
    public void Siblings_Sorted_By_OrderNo_Then_Title()
    {
        var a = NewMenu(Guid.NewGuid(), path: "/a", orderNo: 20);
        var b = NewMenu(Guid.NewGuid(), path: "/b", orderNo: 10);
        var c = NewMenu(Guid.NewGuid(), path: "/c", orderNo: 10, type: MenuTypeEnum.Menu);

        var tree = Build(new List<Menu> { a, b, c }, Array.Empty<string>());
        // 同 OrderNo 按 Title 序数比较："/b" < "/c"（路径作 Title 兜底场景）
        tree.Select(x => x.Path).ShouldBe(new[] { "/b", "/c", "/a" });
    }
}
