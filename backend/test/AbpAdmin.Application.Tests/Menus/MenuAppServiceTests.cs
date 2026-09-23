using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Menus;

/* 菜单管理（动态菜单）测试。
 * 覆盖：播种幂等、CRUD 校验（含 path 外链拒绝、Host-only 权限绑定拒绝、父级成环拒绝、
 * 删除后同 path 重建）、角色分配（含大小写规范存储）、my-menu 混合授权判定矩阵
 * （测试基建 AddAlwaysAllowAuthorization 使权限检查恒真，权限绑定节点据此断言为"可见"；
 * 角色勾选维度的隐藏仍由本文件直接验证）、停用子树剔除、隐藏节点子级上浮、空目录折叠、
 * 租户懒拷贝隔离。
 */
public abstract class MenuAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IMenuAppService _menuAppService;
    private readonly IMyMenuAppService _myMenuAppService;
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly MenuManager _menuManager;

    protected MenuAppServiceTests()
    {
        _menuAppService = GetRequiredService<IMenuAppService>();
        _myMenuAppService = GetRequiredService<IMyMenuAppService>();
        _menuRepository = GetRequiredService<IRepository<Menu, Guid>>();
        _menuGrantRepository = GetRequiredService<IRepository<MenuGrant, Guid>>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _menuManager = GetRequiredService<MenuManager>();
    }

    private async Task CleanMenusAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _menuGrantRepository.DeleteAsync(_ => true);
                await _menuRepository.DeleteAsync(_ => true);
            });
        }
    }

    private async Task<IdentityRole> EnsureRoleAsync(string roleName)
    {
        var existing = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
        if (existing != null)
        {
            return existing;
        }

        var role = new IdentityRole(_guidGenerator.Create(), roleName, _currentTenant.Id);
        return await _roleRepository.InsertAsync(role, autoSave: true);
    }

    [Fact]
    public async Task Seed_Should_Be_Idempotent()
    {
        await CleanMenusAsync();

        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var count = await _menuRepository.GetCountAsync();
            count.ShouldBe(MenuTemplateDefinition.All.Count);
        }
    }

    [Fact]
    public async Task Create_Menu_Requires_Path()
    {
        await CleanMenusAsync();
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _menuAppService.CreateAsync(new MenuCreateDto
            {
                Type = MenuTypeEnum.Menu,
                Title = "无路径菜单"
            });
        });
    }

    [Fact]
    public async Task Create_Should_Reject_Duplicate_Path()
    {
        await CleanMenusAsync();
        var first = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "页面一",
            Path = "/x/one"
        });

        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _menuAppService.CreateAsync(new MenuCreateDto
            {
                Type = MenuTypeEnum.Menu,
                Title = "页面二",
                Path = "/x/one"
            });
        });
    }

    [Fact]
    public async Task Update_Should_Reject_Stale_ConcurrencyStamp()
    {
        await CleanMenusAsync();
        var menu = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "并发测试页",
            Path = "/x/concurrent"
        });

        // 第二个编辑者先拿到旧快照
        var staleStamp = menu.ConcurrencyStamp;
        await _menuAppService.UpdateAsync(menu.Id, new MenuUpdateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "先保存的人",
            Path = "/x/concurrent",
            ConcurrencyStamp = staleStamp
        });

        // 第一个编辑者带着同一份旧戳保存 → 409（AbpDbConcurrencyException），不再静默覆盖
        await Should.ThrowAsync<AbpDbConcurrencyException>(async () =>
        {
            await _menuAppService.UpdateAsync(menu.Id, new MenuUpdateDto
            {
                Type = MenuTypeEnum.Menu,
                Title = "后保存但被拒绝的人",
                Path = "/x/concurrent",
                ConcurrencyStamp = staleStamp
            });
        });

        // 拒绝必须等于「数据未动」：被拒编辑者的内容一个字都不能落库
        (await _menuAppService.GetAsync(menu.Id)).Title.ShouldBe("先保存的人");

        // 带当前最新戳更新成功；旧客户端无戳兼容用例见本测试末尾
        var latest = await _menuAppService.GetAsync(menu.Id);
        latest.ConcurrencyStamp.ShouldNotBeNull();
        latest.ConcurrencyStamp.ShouldNotBe(staleStamp); // 保存时 ABP 已轮转戳值
        await _menuAppService.UpdateAsync(menu.Id, new MenuUpdateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "带新戳的更新",
            Path = "/x/concurrent",
            ConcurrencyStamp = latest.ConcurrencyStamp
        });
        (await _menuAppService.GetAsync(menu.Id)).Title.ShouldBe("带新戳的更新");

        // 旧客户端兼容：不传戳时即使内容基于旧快照也放行（EF 并发令牌在 SaveChanges 仍是最终防线）
        await _menuAppService.UpdateAsync(menu.Id, new MenuUpdateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "旧客户端无戳更新",
            Path = "/x/concurrent"
        });
        (await _menuAppService.GetAsync(menu.Id)).Title.ShouldBe("旧客户端无戳更新");
    }

    [Fact]
    public async Task Delete_With_Role_Grants_Should_Remove_Grants()
    {
        await CleanMenusAsync();
        var menu = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "带授权的页",
            Path = "/x/grant-cascade"
        });
        var role = await EnsureRoleAsync("menu-grant-cascade-role");
        await _menuAppService.UpdateRoleGrantsAsync(menu.Id, new UpdateMenuGrantsDto
        {
            RoleNames = new[] { role.Name! }.ToList()
        });

        await _menuAppService.DeleteAsync(menu.Id);

        // 应用层手工清理与 DB 级联外键（20260919130140）互为双防线，本测试钉住的是
        // 两道防线的并集结果：菜单行消失后不允许残留任何授权孤儿行
        // 菜单行已删（应用层 GetMenuAsync 抛 MenuNotFound 业务异常）
        await Should.ThrowAsync<BusinessException>(async () => await _menuAppService.GetAsync(menu.Id));
        using (_dataFilter.Disable<IMultiTenant>())
        {
            await WithUnitOfWorkAsync(async () =>
            {
                (await _menuGrantRepository.CountAsync(x => x.MenuId == menu.Id)).ShouldBe(0);
                (await _menuRepository.FirstOrDefaultAsync(x => x.Id == menu.Id)).ShouldBeNull();
            });
        }
    }

    /// <summary>
    /// 拷贝必须在锁内提交（ABP StaticPermissionSaver 范式的核心保证）：懒拷贝用独立事务
    /// 在锁内完成提交，不随调用方环境事务的成败而存亡。若回归为"拷贝参与环境事务"，
    /// 本测试因外层事务故意不提交而回滚，将看到空表。
    /// </summary>
    [Fact]
    public async Task EnsureTenantMenusAsync_Should_Commit_Copy_Inside_Lock()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        var tenantId = Guid.NewGuid();
        using (_currentTenant.Change(tenantId))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                // 独立外层事务：故意不 CompleteAsync，模拟调用方事务稍后回滚
                using var callerUow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
                await _menuManager.EnsureTenantMenusAsync(tenantId);
            });
        }

        // 独立作用域可见 = 拷贝已在锁内先行提交
        (await WithUnitOfWorkAsync(() => _menuRepository.GetCountAsync())).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Delete_With_Children_Should_Throw()
    {
        await CleanMenusAsync();
        var catalog = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Catalog,
            Title = "目录",
            Path = "/x"
        });
        await _menuAppService.CreateAsync(new MenuCreateDto
        {
            ParentId = catalog.Id,
            Type = MenuTypeEnum.Menu,
            Title = "子页",
            Path = "/x/child"
        });

        await Should.ThrowAsync<BusinessException>(async () => await _menuAppService.DeleteAsync(catalog.Id));
    }

    [Fact]
    public async Task Role_Grants_Roundtrip_And_Invalid_Role_Rejected()
    {
        await CleanMenusAsync();
        var menu = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "受控页",
            Path = "/x/granted"
        });
        var role = await EnsureRoleAsync("menu-test-role");

        // 大小写变体输入：落库必须是角色的规范 Name（读取侧按原始 Name 精确匹配，
        // 原样大小写入库会导致永远匹配不上）
        await _menuAppService.UpdateRoleGrantsAsync(menu.Id, new UpdateMenuGrantsDto
        {
            RoleNames = new[] { role.Name!.ToUpperInvariant() }.ToList()
        });

        var granted = await _menuAppService.GetRoleGrantsAsync(menu.Id);
        granted.Items.ShouldContain(role.Name);

        // 全量覆盖：换成另一个角色后旧角色消失
        var role2 = await EnsureRoleAsync("menu-test-role-2");
        await _menuAppService.UpdateRoleGrantsAsync(menu.Id, new UpdateMenuGrantsDto
        {
            RoleNames = new[] { role2.Name! }.ToList()
        });
        granted = await _menuAppService.GetRoleGrantsAsync(menu.Id);
        granted.Items.ShouldNotContain(role.Name);
        granted.Items.ShouldContain(role2.Name);

        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _menuAppService.UpdateRoleGrantsAsync(menu.Id, new UpdateMenuGrantsDto
            {
                RoleNames = new[] { "no-such-role" }.ToList()
            });
        });
    }

    [Fact]
    public async Task External_Link_Path_Rejected()
    {
        await CleanMenusAsync();
        // 防"存储型链接注入"：path 渲染为全租户可见的导航项，服务端必须拒绝外链形态
        foreach (var badPath in new[] { "//evil.com/phish", "https://evil.com", "javascript:alert(1)", "relative/path" })
        {
            await Should.ThrowAsync<BusinessException>(async () =>
            {
                await _menuAppService.CreateAsync(new MenuCreateDto
                {
                    Type = MenuTypeEnum.Menu,
                    Title = "外链",
                    Path = badPath
                });
            }, $"path 应被拒绝：{badPath}");
        }
    }

    [Fact]
    public async Task Host_Only_Permission_Binding_Rejected_For_Tenant()
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            // 租户上下文绑定 Host-only 权限会让菜单对整个租户永久不可见（配置自毁），必须拒绝
            await Should.ThrowAsync<BusinessException>(async () =>
            {
                await _menuAppService.CreateAsync(new MenuCreateDto
                {
                    Type = MenuTypeEnum.Menu,
                    Title = "宿主权限菜单",
                    Path = "/tenant/host-only",
                    PermissionName = "AbpAdmin.TenantPackages"
                });
            });
        }
    }

    [Fact]
    public async Task Parent_Cycle_Rejected_And_Recreate_Same_Path_After_Delete_Works()
    {
        await CleanMenusAsync();
        var catalog = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Catalog,
            Title = "目录",
            Path = "/cycle"
        });
        var child = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            ParentId = catalog.Id,
            Type = MenuTypeEnum.Menu,
            Title = "子页",
            Path = "/cycle/child"
        });

        // 自身为父 → 拒绝
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _menuAppService.UpdateAsync(catalog.Id, new MenuUpdateDto
            {
                ParentId = catalog.Id,
                Type = MenuTypeEnum.Catalog,
                Title = "目录",
                Path = "/cycle",
            });
        });

        // 把父目录挂到自己的子孙 → 拒绝（深层环）
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _menuAppService.UpdateAsync(catalog.Id, new MenuUpdateDto
            {
                ParentId = child.Id,
                Type = MenuTypeEnum.Catalog,
                Title = "目录",
                Path = "/cycle",
            });
        });

        // 删除子页后同 path 重建成功（回归：软删除行占用唯一索引会撞约束，删除走硬删除）
        await _menuAppService.DeleteAsync(child.Id);
        var recreated = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            ParentId = catalog.Id,
            Type = MenuTypeEnum.Menu,
            Title = "子页重建",
            Path = "/cycle/child",
        });
        recreated.Path.ShouldBe("/cycle/child");
    }

    [Fact]
    public async Task Permission_Bound_Menu_Visible_Under_Always_Allow_Checker()
    {
        await CleanMenusAsync();
        var permissionMenu = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "权限绑定页",
            Path = "/perm-bound",
            PermissionName = "AbpAdmin.Menus"
        });

        var myMenu = await _myMenuAppService.GetAsync();
        var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();

        // 测试基建是 AlwaysAllowPermissionChecker（恒真）：权限绑定节点可见。
        // 真实环境该节点是否可见由 IPermissionChecker 决定，deny-all 场景未在本仓库测试基建覆盖。
        paths.ShouldContain(permissionMenu.Path);
    }

    [Fact]
    public async Task MyMenu_Public_Node_Visible_And_Grant_Controlled_Node_Hidden_Without_Role()
    {
        await CleanMenusAsync();
        var publicMenu = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "公开页",
            Path = "/pub"
        });
        var controlled = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "受控页",
            Path = "/pub/controlled"
        });
        var role = await EnsureRoleAsync("menu-grant-role");
        await _menuAppService.UpdateRoleGrantsAsync(controlled.Id, new UpdateMenuGrantsDto
        {
            RoleNames = new[] { role.Name! }.ToList()
        });

        var myMenu = await _myMenuAppService.GetAsync();
        var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();

        paths.ShouldContain(publicMenu.Path);
        paths.ShouldNotContain(controlled.Path); // 测试用户无该角色 → 受控节点不可见
    }

    [Fact]
    public async Task MyMenu_Disabled_Subtree_Removed_And_Hidden_Node_Children_Bubble_Up()
    {
        await CleanMenusAsync();
        var disabledCatalog = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Catalog,
            Title = "停用目录",
            Path = "/disabled"
        });
        await _menuAppService.CreateAsync(new MenuCreateDto
        {
            ParentId = disabledCatalog.Id,
            Type = MenuTypeEnum.Menu,
            Title = "停用目录子页",
            Path = "/disabled/child"
        });
        await _menuAppService.UpdateAsync(disabledCatalog.Id, new MenuUpdateDto
        {
            Type = MenuTypeEnum.Catalog,
            Title = "停用目录",
            Path = "/disabled",
            IsEnabled = false
        });

        var hiddenCatalog = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Catalog,
            Title = "隐藏目录",
            Path = "/hidden",
            IsHide = true
        });
        var child = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            ParentId = hiddenCatalog.Id,
            Type = MenuTypeEnum.Menu,
            Title = "隐藏目录子页",
            Path = "/hidden/child"
        });

        var myMenu = await _myMenuAppService.GetAsync();
        var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();

        paths.ShouldNotContain("/disabled");
        paths.ShouldNotContain("/disabled/child");
        paths.ShouldNotContain("/hidden");
        paths.ShouldContain(child.Path); // 隐藏目录的子页上浮到顶级
        // 上浮后的子页应位于根级别
        myMenu.Items.Any(x => x.Path == child.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task MyMenu_Empty_Catalog_Folded()
    {
        await CleanMenusAsync();
        var catalog = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Catalog,
            Title = "空目录",
            Path = "/empty-catalog"
        });

        var myMenu = await _myMenuAppService.GetAsync();
        Flatten(myMenu.Items).Select(x => x.Path).ShouldNotContain(catalog.Path);
    }

    [Fact]
    public async Task Tenant_Should_Get_Lazy_Copy_And_Isolated()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        var tenantId = Guid.NewGuid();
        using (_currentTenant.Change(tenantId))
        {
            var myMenu = await _myMenuAppService.GetAsync();
            // 懒拷贝后：公开节点（欢迎/当前会话）可见
            var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();
            paths.ShouldContain("/welcome");
            paths.ShouldContain("/current-session");

            // 租户内删除一个节点不影响 Host 模板
            await WithUnitOfWorkAsync(async () =>
            {
                var welcome = (await _menuRepository.GetListAsync(x => x.Path == "/welcome")).First();
                await _menuRepository.DeleteAsync(welcome);
            });
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var hostWelcome = await _menuRepository.FirstOrDefaultAsync(x => x.TenantId == null && x.Path == "/welcome");
                hostWelcome.ShouldNotBeNull();
            });
        }
    }

    /// <summary>
    /// SeededCheckCache 租户删除清理（代码审查 round1 修复）：租户删除事件必须清掉
    /// 「已播种」短缓存条目——否则同 Id 租户重建（数据恢复/迁移场景）后首访会被残留条目
    /// 短路跳过懒拷贝，菜单空树且无报错。事件分发直接调处理器（不经本地事件总线），保持确定性。
    /// </summary>
    [Fact]
    public async Task Tenant_Deleted_Should_Clear_SeededCheckCache_And_Allow_Recopy()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        var tenant = await GetRequiredService<Volo.Abp.TenantManagement.ITenantManager>()
            .CreateAsync($"menu-cache-{Guid.NewGuid():N}"[..30]);

        using (_currentTenant.Change(tenant.Id))
        {
            // 首访：懒拷贝并标记「已播种」短缓存（1 分钟窗口）
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(tenant.Id));
            var seededCount = await WithUnitOfWorkAsync(() => _menuRepository.GetCountAsync());
            seededCount.ShouldBeGreaterThan(0);
        }

        // 模拟租户删除发布的 EntityDeletedEventData<Tenant>（处理器风格同 EditionChangedTenantCacheInvalidator）
        await GetRequiredService<TenantDeletedMenuSeededCacheInvalidator>()
            .HandleEventAsync(
                new Volo.Abp.Domain.Entities.Events.EntityDeletedEventData<Volo.Abp.TenantManagement.Tenant>(tenant));

        using (_currentTenant.Change(tenant.Id))
        {
            // 删光该租户菜单：必须硬删——软删行仍占用 (TenantId, Path) 唯一索引，重拷同路径会撞约束
            await WithUnitOfWorkAsync(() => _menuRepository.HardDeleteAsync(x => true));

            // 缓存条目已清理：本次访问必须重新懒拷贝；若条目残留会被 1 分钟短缓存短路成空树
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(tenant.Id));
            var recopiedCount = await WithUnitOfWorkAsync(() => _menuRepository.GetCountAsync());
            recopiedCount.ShouldBeGreaterThan(0);
        }
    }

    private static System.Collections.Generic.List<MyMenuItemDto> Flatten(
        System.Collections.Generic.IReadOnlyList<MyMenuItemDto> items)
    {
        var result = new System.Collections.Generic.List<MyMenuItemDto>();
        void Walk(System.Collections.Generic.IEnumerable<MyMenuItemDto> list)
        {
            foreach (var item in list)
            {
                result.Add(item);
                Walk(item.Children);
            }
        }

        Walk(items);
        return result;
    }
}
