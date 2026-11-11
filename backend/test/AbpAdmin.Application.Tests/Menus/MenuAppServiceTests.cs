using System;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Menus;

/* 菜单管理（动态菜单）测试。
 * 覆盖：播种幂等、CRUD 校验（含 path 外链拒绝、Host-only 权限绑定拒绝、父级成环拒绝、
 * 删除后同 path 重建）、my-menu ABP 权限判定
 * （默认测试基建 AddAlwaysAllowAuthorization；拒绝场景仅替换权限检查边界）、
 * 停用子树剔除、隐藏节点子级上浮、空目录折叠、
 * 租户懒拷贝隔离。
 */
public abstract class MenuAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IMenuAppService _menuAppService;
    private readonly IMyMenuAppService _myMenuAppService;
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly MenuManager _menuManager;

    protected MenuAppServiceTests()
    {
        _menuAppService = GetRequiredService<IMenuAppService>();
        _myMenuAppService = GetRequiredService<IMyMenuAppService>();
        _menuRepository = GetRequiredService<IRepository<Menu, Guid>>();
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
                await _menuRepository.DeleteAsync(_ => true);
            });
        }
    }

    private async Task<Menu> CreateMenuAsync(string title, string? path = null, int orderNo = 100)
    {
        var menu = new Menu(_guidGenerator.Create(), null, null,
            path == null ? MenuTypeEnum.Catalog : MenuTypeEnum.Menu,
            title, name: null, path: path, orderNo: orderNo);
        await WithUnitOfWorkAsync(() => _menuRepository.InsertAsync(menu, autoSave: true));
        return menu;
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

    /* 模板同步语义（菜单重排引入）：SeedHostTemplateAsync 不只补缺——已有节点会被对齐到
     * 模板的父/序/题/图标/权限，模板重排后重跑迁移即生效；模板外的自定义节点不被删除。 */
    [Fact]
    public async Task Seed_Should_Sync_Existing_Nodes_To_Template()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        // 模拟旧布局：SaaS 和示例是顶层组，设置在管理目录，收付款目录绑定订单权限。
        var existingIdsByPath = await WithUnitOfWorkAsync(async () =>
        {
            var all = await _menuRepository.GetListAsync();
            var byPath = all.ToDictionary(x => x.Path!);
            var administration = byPath["/administration"];
            var saas = byPath["/administration/saas"];
            saas.SetParent(null);
            saas.SetOrderNo(60);
            saas.SetTitle("SaaS");
            await _menuRepository.UpdateAsync(saas);

            var demos = byPath["/demos"];
            demos.SetParent(null);
            await _menuRepository.UpdateAsync(demos);

            var settings = byPath["/administration/settings"];
            settings.SetParent(administration.Id);
            await _menuRepository.UpdateAsync(settings);
            await _menuRepository.HardDeleteAsync(x => x.Path == "/administration/base-settings");

            var collection = byPath["/collection"];
            collection.SetParent(null);
            collection.SetPermissionName("AbpAdmin.Ordering.Orders");
            await _menuRepository.UpdateAsync(collection);

            var custom = new Menu(
                _guidGenerator.Create(), null, saas.Id, MenuTypeEnum.Menu,
                "自定义节点", "custom", "/custom-page", null, 999, false, isEnabled: true);
            await _menuRepository.InsertAsync(custom);
            byPath.Add(custom.Path!, custom);
            byPath.Remove("/administration/base-settings");
            return byPath.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
        });

        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        await WithUnitOfWorkAsync(async () =>
        {
            var all = await _menuRepository.GetListAsync();
            var byPath = all.ToDictionary(x => x.Path!);
            foreach (var (path, id) in existingIdsByPath)
            {
                byPath[path].Id.ShouldBe(id);
            }

            var saas = byPath["/administration/saas"];
            saas.ParentId.ShouldBe(byPath["/administration"].Id);
            saas.OrderNo.ShouldBe(MenuTemplateDefinition.All.Single(d => d.Key == "saas").OrderNo);
            saas.Title.ShouldBe("租户与功能");
            byPath["/custom-page"].ParentId.ShouldBe(saas.Id);
            byPath["/demos"].ParentId.ShouldBe(byPath["/system"].Id);
            byPath["/administration/settings"].ParentId.ShouldBe(byPath["/administration/base-settings"].Id);
            byPath["/collection"].ParentId.ShouldBe(byPath["/business"].Id);
            byPath["/collection"].PermissionName.ShouldBeNull();
        });
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
        // 权限拒绝路径由 MyMenu_Permission_Bound_Node_Hidden_When_Abp_Permission_Denied 覆盖。
        paths.ShouldContain(permissionMenu.Path);
    }

    [Fact]
    public async Task MyMenu_Unbound_And_Abp_Permission_Granted_Nodes_Are_Visible()
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
            Title = "权限绑定页",
            Path = "/pub/controlled",
            PermissionName = "AbpAdmin.Menus"
        });
        var myMenu = await _myMenuAppService.GetAsync();
        var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();

        paths.ShouldContain(publicMenu.Path);
        paths.ShouldContain(controlled.Path);
    }

    [Fact]
    public async Task MyMenu_Permission_Bound_Node_Hidden_When_Abp_Permission_Denied()
    {
        await CleanMenusAsync();
        var permissionMenu = await _menuAppService.CreateAsync(new MenuCreateDto
        {
            Type = MenuTypeEnum.Menu,
            Title = "缺少权限的菜单",
            Path = "/permission-denied",
            PermissionName = "AbpAdmin.Menus"
        });
        var publicMenu = await CreateMenuAsync("登录可见菜单", "/public-menu");
        // 仅替换权限检查边界：仍使用真实仓储、菜单管理器与用户菜单计算路径。
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string[]>()).Returns(call =>
        {
            var grants = new MultiplePermissionGrantResult();
            foreach (var name in call.Arg<string[]>())
            {
                grants.Result[name] = PermissionGrantResult.Prohibited;
            }
            return Task.FromResult(grants);
        });
        var service = new MyMenuAppService(_menuRepository, _menuManager, checker)
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
        };
        var myMenu = await WithUnitOfWorkAsync(() => service.GetAsync());
        var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();

        paths.ShouldNotContain(permissionMenu.Path);
        paths.ShouldContain(publicMenu.Path);
    }

    [Fact]
    public async Task MyMenu_Keeps_Collection_Group_When_Only_Collection_Permission_Is_Granted()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string[]>()).Returns(call =>
        {
            var grants = new MultiplePermissionGrantResult();
            foreach (var name in call.Arg<string[]>())
            {
                grants.Result[name] = name == "AbpAdmin.Collection.Collections"
                    ? PermissionGrantResult.Granted
                    : PermissionGrantResult.Prohibited;
            }

            return Task.FromResult(grants);
        });
        var service = new MyMenuAppService(_menuRepository, _menuManager, checker)
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
        };

        var myMenu = await WithUnitOfWorkAsync(() => service.GetAsync());
        myMenu.Items.Select(x => x.Path).ShouldBe(new[] { "/welcome", "/business" });
        var business = myMenu.Items.Single(x => x.Path == "/business");
        var collection = business.Children.ShouldHaveSingleItem();
        collection.Path.ShouldBe("/collection");
        collection.Children.Select(x => x.Path).ShouldBe(new[]
        {
            "/collection/dashboard",
            "/collection/deposits",
            "/collection/sweeps",
            "/collection/vault",
            "/collection/member-funds"
        });
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
            // 懒拷贝包含现行模板节点，不包含已退役调试页。
            var paths = Flatten(myMenu.Items).Select(x => x.Path).ToList();
            paths.ShouldContain("/system/server-monitor");
            paths.ShouldNotContain("/current-session");

            // 租户内删除一个节点不影响 Host 模板
            await WithUnitOfWorkAsync(async () =>
            {
                var serverMonitor = (await _menuRepository.GetListAsync(x => x.Path == "/system/server-monitor")).First();
                await _menuRepository.DeleteAsync(serverMonitor);
            });
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var hostNode = await _menuRepository.FirstOrDefaultAsync(x => x.TenantId == null && x.Path == "/system/server-monitor");
                hostNode.ShouldNotBeNull();
            });
        }
    }

    /// <summary>
    /// SeededCheckCache 租户删除清理（bug 修复）：租户删除事件必须清掉
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
