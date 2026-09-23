using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Tenants;

/* 租户套餐测试：CRUD、菜单勾选全量替换、应用套餐后租户菜单树被过滤（祖先链保留）、
 * 应用空套餐/不存在套餐的失败路径、PackageId 强标识持久化、懒拷贝按套餐收敛、
 * 租户内访问套餐管理被拒。
 */
public abstract class TenantPackageAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITenantPackageAppService _packageAppService;
    private readonly IMenuAppService _menuAppService;
    private readonly IRepository<TenantPackage, Guid> _packageRepository;
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly MenuManager _menuManager;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;

    protected TenantPackageAppServiceTests()
    {
        _packageAppService = GetRequiredService<ITenantPackageAppService>();
        _menuAppService = GetRequiredService<IMenuAppService>();
        _packageRepository = GetRequiredService<IRepository<TenantPackage, Guid>>();
        _menuRepository = GetRequiredService<IRepository<Menu, Guid>>();
        _menuManager = GetRequiredService<MenuManager>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private async Task CleanAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await _menuRepository.DeleteAsync(x => x.TenantId != null);
                await _menuRepository.DeleteAsync(x => true);
            });
        }

        await WithUnitOfWorkAsync(() => _packageRepository.DeleteAsync(_ => true));
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
    }

    [Fact]
    public async Task Crud_And_Name_Unique()
    {
        await CleanAsync();
        var created = await _packageAppService.CreateAsync(new TenantPackageCreateDto
        {
            Name = "基础版",
            Remark = "测试套餐"
        });
        created.Name.ShouldBe("基础版");

        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _packageAppService.CreateAsync(new TenantPackageCreateDto { Name = "基础版" });
        });

        var updated = await _packageAppService.UpdateAsync(created.Id, new TenantPackageUpdateDto
        {
            Name = "标准版"
        });
        updated.Name.ShouldBe("标准版");

        var list = await _packageAppService.GetListAsync(new TenantPackageListInput { Filter = "标准" });
        list.TotalCount.ShouldBe(1);

        await _packageAppService.DeleteAsync(created.Id);
        (await _packageRepository.GetCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Menu_Selection_Roundtrip_And_Invalid_Id_Rejected()
    {
        await CleanAsync();
        var package = await _packageAppService.CreateAsync(new TenantPackageCreateDto { Name = "选择套餐" });

        List<Menu> template;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            template = await _menuRepository.GetListAsync(x => x.TenantId == null);
        }

        var usersNode = template.First(x => x.Path == "/administration/identity/users");
        var welcome = template.First(x => x.Path == "/welcome");
        await _packageAppService.UpdateMenuSelectionAsync(package.Id, new UpdateTenantPackageMenusDto
        {
            MenuIds = new List<Guid> { welcome.Id, usersNode.Id }
        });

        var selection = await _packageAppService.GetMenuSelectionAsync(package.Id);
        selection.CheckedMenuIds.Count.ShouldBe(2);

        // 换成不存在于模板的 id → 拒绝
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _packageAppService.UpdateMenuSelectionAsync(package.Id, new UpdateTenantPackageMenusDto
            {
                MenuIds = new List<Guid> { Guid.NewGuid() }
            });
        });
    }

    [Fact]
    public async Task Apply_Package_Filters_Tenant_Menu_Tree()
    {
        await CleanAsync();
        var package = await _packageAppService.CreateAsync(new TenantPackageCreateDto { Name = "受限套餐" });

        List<Menu> template;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            template = await _menuRepository.GetListAsync(x => x.TenantId == null);
        }

        // 勾选：欢迎 + 用户（其祖先链 管理/身份管理 应自动保留），不勾 系统/当前会话 等
        var welcome = template.First(x => x.Path == "/welcome");
        var usersNode = template.First(x => x.Path == "/administration/identity/users");
        await _packageAppService.UpdateMenuSelectionAsync(package.Id, new UpdateTenantPackageMenusDto
        {
            MenuIds = new List<Guid> { welcome.Id, usersNode.Id }
        });

        var tenantId = Guid.NewGuid();

        // 勾选集在 Host 上下文取（套餐接口 Host 专属）
        var selection = await _packageAppService.GetMenuSelectionAsync(package.Id);
        var allowed = selection.CheckedMenuIds.ToHashSet();

        using (_currentTenant.Change(tenantId))
        {
            // 全量懒拷贝
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(tenantId));
            var fullCount = await _menuRepository.GetCountAsync();

            // 应用套餐：重置为过滤后的拷贝
            await WithUnitOfWorkAsync(() => _menuManager.ResetTenantMenusAsync(tenantId, allowed));

            var tenantMenus = await _menuRepository.GetListAsync();
            ((long)tenantMenus.Count).ShouldBeLessThan(fullCount);

            var paths = tenantMenus.Select(x => x.Path).ToList();
            paths.ShouldContain("/welcome");
            paths.ShouldContain("/administration/identity/users");
            paths.ShouldContain("/administration/identity"); // 祖先链
            paths.ShouldContain("/administration");
            paths.ShouldNotContain("/current-session");
            paths.ShouldNotContain("/administration/identity/roles");
        }
    }

    [Fact]
    public async Task Tenant_Context_Rejected()
    {
        await CleanAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<BusinessException>(async () =>
            {
                await _packageAppService.GetListAsync(new TenantPackageListInput());
            });
        }
    }

    [Fact]
    public async Task Apply_Package_Via_Tenant_Service_Resets_And_Persists_PackageId()
    {
        await CleanAsync();
        // ApplyPackageAsync 是本类新增端点（不在开源 ITenantAppService 契约上），解析具体类调用
        var tenantAppService = GetRequiredService<AbpAdmin.Tenants.TenantAppService>();
        var tenantRepository = GetRequiredService<Volo.Abp.TenantManagement.ITenantRepository>();

        // 套餐勾选：欢迎 + 用户
        var package = await _packageAppService.CreateAsync(new TenantPackageCreateDto { Name = "应用套餐" });
        List<Menu> template;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            template = await _menuRepository.GetListAsync(x => x.TenantId == null);
        }
        var welcome = template.First(x => x.Path == "/welcome");
        var usersNode = template.First(x => x.Path == "/administration/identity/users");
        await _packageAppService.UpdateMenuSelectionAsync(package.Id, new UpdateTenantPackageMenusDto
        {
            MenuIds = new List<Guid> { welcome.Id, usersNode.Id }
        });

        var tenantManager = GetRequiredService<Volo.Abp.TenantManagement.ITenantManager>();
        var tenant = await tenantManager.CreateAsync("apply-pkg-test");
        var tenantId = tenant.Id;
        await WithUnitOfWorkAsync(() => tenantRepository.InsertAsync(tenant));

        // 空套餐守卫：未勾选菜单的套餐拒绝应用（防一键清空租户菜单树的唯一防线）
        var emptyPackage = await _packageAppService.CreateAsync(new TenantPackageCreateDto { Name = "空套餐" });
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await tenantAppService.ApplyPackageAsync(tenantId, new ApplyTenantPackageDto { PackageId = emptyPackage.Id });
        });

        // 不存在的套餐 → 明确业务错误码
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await tenantAppService.ApplyPackageAsync(tenantId, new ApplyTenantPackageDto { PackageId = Guid.NewGuid() });
        });

        // 租户先拿到全量懒拷贝 + 一个自定义菜单（应用套餐应把它清掉）
        using (_currentTenant.Change(tenantId))
        {
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(tenantId));
            await WithUnitOfWorkAsync(() => _menuAppService.CreateAsync(new MenuCreateDto
            {
                Type = MenuTypeEnum.Menu,
                Title = "自定义页",
                Path = "/custom-page",
            }));
        }

        await tenantAppService.ApplyPackageAsync(tenantId, new ApplyTenantPackageDto { PackageId = package.Id });

        // PackageId 强标识持久化在租户上
        var reloadedTenant = await tenantRepository.GetAsync(tenantId);
        reloadedTenant.GetProperty<string>(AbpAdminTenantConsts.PackageIdPropertyName).ShouldBe(package.Id.ToString());

        using (_currentTenant.Change(tenantId))
        {
            var paths = (await _menuRepository.GetListAsync()).Select(x => x.Path).ToList();
            paths.ShouldNotContain("/custom-page"); // 破坏性重置清掉自定义菜单
            paths.ShouldContain("/welcome");
            paths.ShouldContain("/administration/identity/users");
            paths.ShouldNotContain("/current-session");
        }
    }

    [Fact]
    public async Task Lazy_Copy_Converges_To_Package_Without_Explicit_Apply()
    {
        await CleanAsync();
        var tenantRepository = GetRequiredService<Volo.Abp.TenantManagement.ITenantRepository>();
        var packageRepository = GetRequiredService<IRepository<TenantPackage, Guid>>();

        var package = new TenantPackage(Guid.NewGuid(), "懒拷贝套餐");
        List<Menu> template;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            template = await _menuRepository.GetListAsync(x => x.TenantId == null);
        }
        var welcome = template.First(x => x.Path == "/welcome");
        package.SetMenus(new[] { welcome.Id }, GetRequiredService<Volo.Abp.Guids.IGuidGenerator>());
        await WithUnitOfWorkAsync(() => packageRepository.InsertAsync(package, autoSave: true));

        // 租户只配了 PackageId（模拟 apply-package 第二步失败的补偿场景），从不显式应用
        var tenantManager2 = GetRequiredService<Volo.Abp.TenantManagement.ITenantManager>();
        var tenant2 = await tenantManager2.CreateAsync("lazy-pkg-test");
        var tenantId = tenant2.Id;
        tenant2.SetProperty(AbpAdminTenantConsts.PackageIdPropertyName, package.Id.ToString());
        await WithUnitOfWorkAsync(() => tenantRepository.InsertAsync(tenant2));

        using (_currentTenant.Change(tenantId))
        {
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(tenantId));
            var paths = (await _menuRepository.GetListAsync()).Select(x => x.Path).ToList();
            paths.ShouldContain("/welcome");
            paths.ShouldNotContain("/current-session"); // 懒拷贝直接按套餐收敛，而不是全量
        }
    }
}
