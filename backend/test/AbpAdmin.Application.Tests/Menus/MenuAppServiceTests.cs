using System;
using System.Collections.Generic;
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
using AbpAdmin.Tenants;
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

    [Fact]
    public async Task Upgrade_Should_Move_Old_Defaults_And_Preserve_Ids_And_Customizations()
    {
        await CleanMenusAsync();
        var platform = LegacyMenu(null, MenuTypeEnum.Catalog, "平台管理", "administration", "/administration", 800);
        var config = LegacyMenu(null, MenuTypeEnum.Catalog, "设置中心", "configuration", "/configuration", 1000);
        var baseSettings = LegacyMenu(config.Id, MenuTypeEnum.Catalog, "平台设置", "base-settings", "/administration/base-settings", 1040);
        var settings = LegacyMenu(baseSettings.Id, MenuTypeEnum.Menu, "通用设置", "settings", "/administration/settings", 141, "SettingUi.ShowSettingPage");
        var menus = LegacyMenu(baseSettings.Id, MenuTypeEnum.Menu, "自定义菜单标题", "menus", "/administration/menus", 1234, "AbpAdmin.Menus");
        var financeConfig = LegacyMenu(config.Id, MenuTypeEnum.Catalog, "会计设置", "finance-config", "/finance/configuration", 1030);
        var rules = LegacyMenu(financeConfig.Id, MenuTypeEnum.Menu, "自动记账规则", "finance-rules", "/finance/rules", 662, "Finance.Rule.View");
        financeConfig.Disable();
        await WithUnitOfWorkAsync(() => _menuRepository.InsertManyAsync([platform, config, baseSettings, settings, menus, financeConfig, rules]));

        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        await WithUnitOfWorkAsync(async () =>
        {
            var byPath = (await _menuRepository.GetListAsync()).ToDictionary(x => x.Path!);
            byPath[config.Path!].IsHide.ShouldBeTrue();
            byPath[settings.Path!].Id.ShouldBe(settings.Id);
            byPath[settings.Path!].Title.ShouldBe("扩展配置");
            byPath[settings.Path!].ParentId.ShouldBe(platform.Id);
            byPath[settings.Path!].PermissionName.ShouldBe("SettingUi.ShowSettingPage");
            byPath[settings.Path!].GetTemplateKey().ShouldBe("settings");
            byPath[menus.Path!].Id.ShouldBe(menus.Id);
            byPath[menus.Path!].Title.ShouldBe("自定义菜单标题");
            byPath[menus.Path!].OrderNo.ShouldBe(1234);
            byPath[rules.Path!].ParentId.ShouldBe(financeConfig.Id);
            byPath[financeConfig.Path!].IsEnabled.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Tenant_Upgrade_Should_Retain_Only_The_Previously_Selected_Settings_Leaf()
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var config = LegacyMenu(null, MenuTypeEnum.Catalog, "设置中心", "configuration", "/configuration", 1000);
            var baseSettings = LegacyMenu(config.Id, MenuTypeEnum.Catalog, "平台设置", "base-settings", "/administration/base-settings", 1040);
            var settings = LegacyMenu(baseSettings.Id, MenuTypeEnum.Menu, "通用设置", "settings", "/administration/settings", 141, "SettingUi.ShowSettingPage");
            await WithUnitOfWorkAsync(() => _menuRepository.InsertManyAsync([config, baseSettings, settings]));

            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));

            await WithUnitOfWorkAsync(async () =>
            {
                var byPath = (await _menuRepository.GetListAsync()).ToDictionary(x => x.Path!);
                byPath.Values.Where(x => x.Type == MenuTypeEnum.Menu).Select(x => x.Id).ShouldBe([settings.Id]);
                byPath[settings.Path!].ParentId.ShouldBe(byPath["/administration"].Id);
                byPath[settings.Path!].IsHide.ShouldBeFalse();
                byPath[settings.Path!].PermissionName.ShouldBe("SettingUi.ShowSettingPage");
            });
        }
    }

    private Menu LegacyMenu(Guid? parentId, MenuTypeEnum type, string title, string name, string path,
        int order, string? permission = null) => new(_guidGenerator.Create(), _currentTenant.Id, parentId,
        type, title, name, path, orderNo: order, permissionName: permission);

    [Fact]
    public async Task Fresh_Template_Should_Have_Five_Task_Roots_And_Embedded_Finance_Configuration()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        var visible = await WithUnitOfWorkAsync(() => _myMenuAppService.GetAsync());
        visible.Items.Select(x => x.Title).ShouldBe(["工作台", "业务中心", "资金与财务", "平台管理", "系统运维"]);
        var paths = Flatten(visible.Items).Select(x => x.Path).ToList();
        paths.ShouldNotContain("/order-payments");
        paths.ShouldNotContain("/administration/settings");
        paths.ShouldNotContain("/finance/settings");
        paths.ShouldNotContain("/finance/accounts");
        paths.ShouldNotContain("/finance/rules");
        paths.ShouldNotContain("/finance/period");
        visible.Items.Single(x => x.Path == "/business").Children.Select(x => x.Path).ShouldBe(["/collection/orders"]);
        visible.Items.Single(x => x.Path == "/financial-management").Children.Select(x => x.Path)
            .ShouldBe(["/operations/payments", "/user-funds", "/web3/operations", "/finance"]);
        visible.Items.Single(x => x.Path == "/financial-management").Children.Select(x => x.Title)
            .ShouldBe(["收款管理", "用户资金", "钱包与链网", "会计与对账"]);
        Flatten(visible.Items).Single(x => x.Path == "/user-funds").Children.Select(x => x.Path)
            .ShouldBe(["/collection/member-funds", "/collection/withdrawals", "/finance/balance-adjustments"]);
        Flatten(visible.Items).Single(x => x.Path == "/collection/member-funds").Title.ShouldBe("用户余额");
        Flatten(visible.Items).Single(x => x.Path == "/finance/balances").Title.ShouldBe("账簿余额");
    }

    [Fact]
    public async Task V4_Upgrade_Renames_Member_Funds_And_Puts_Withdrawals_Before_Adjustments()
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(4);
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        var visible = await WithUnitOfWorkAsync(() => _myMenuAppService.GetAsync());
        var funds = Flatten(visible.Items).Single(x => x.Path == "/user-funds");
        funds.Children.Select(x => x.Path).ShouldBe([
            "/collection/member-funds", "/collection/withdrawals", "/finance/balance-adjustments"
        ]);
        funds.Children.Select(x => x.Title).ShouldBe(["用户余额", "提现审核与打款", "余额调整审批"]);
        await WithUnitOfWorkAsync(async () =>
        {
            (await _menuRepository.GetAsync(original["collection-member-funds"].Id)).Id
                .ShouldBe(original["collection-member-funds"].Id);
            (await _menuRepository.GetAsync(original["collection-withdrawals"].Id)).OrderNo.ShouldBe(612);
            (await _menuRepository.GetAsync(original["finance-balance-adjustments"].Id)).OrderNo.ShouldBe(613);
        });
    }

    [Fact]
    public async Task V4_Upgrade_Preserves_Custom_Member_Funds_Title_And_Sibling_Order()
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(4);
        await WithUnitOfWorkAsync(async () =>
        {
            var funds = await _menuRepository.GetAsync(original["collection-member-funds"].Id);
            funds.SetTitle("项目余额");
            var withdrawal = await _menuRepository.GetAsync(original["collection-withdrawals"].Id);
            withdrawal.SetOrderNo(42);
            var adjustment = await _menuRepository.GetAsync(original["finance-balance-adjustments"].Id);
            adjustment.SetOrderNo(7);
        });
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(async () =>
        {
            (await _menuRepository.GetAsync(original["collection-member-funds"].Id)).Title.ShouldBe("项目余额");
            (await _menuRepository.GetAsync(original["collection-withdrawals"].Id)).OrderNo.ShouldBe(42);
            (await _menuRepository.GetAsync(original["finance-balance-adjustments"].Id)).OrderNo.ShouldBe(7);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Published_Layouts_Upgrade_To_V5_Without_Replacing_Ids_Paths_Or_Package_References(int version)
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(version);
        var package = new TenantPackage(_guidGenerator.Create(), "版本升级配置套餐");
        package.SetMenus([original["finance-rules"].Id], _guidGenerator);
        var packages = GetRequiredService<IRepository<TenantPackage, Guid>>();
        await WithUnitOfWorkAsync(() => packages.InsertAsync(package));

        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        await WithUnitOfWorkAsync(async () =>
        {
            var current = (await _menuRepository.GetListAsync()).ToDictionary(x => x.GetTemplateKey()!);
            foreach (var (key, old) in original)
            {
                current[key].Id.ShouldBe(old.Id);
                current[key].Path.ShouldBe(old.Path);
                current[key].GetTemplateVersion().ShouldBe(5);
            }
            current["collection-orders"].ParentId.ShouldBe(current["business"].Id);
            current["operations-payments"].ParentId.ShouldBe(current["financial-management"].Id);
            current["web3-operations"].ParentId.ShouldBe(current["financial-management"].Id);
            current["collection-withdrawals"].ParentId.ShouldBe(current["user-funds"].Id);
            current["collection-withdrawals"].IsHide.ShouldBeFalse();
            current["collection-withdrawals"].IsHostOnlyTemplate().ShouldBeTrue();
            current["collection-withdrawals"].PermissionName.ShouldBeNull();
            current["collection-withdrawals"].GetAnyPermissionNames().ShouldBe(
                ["AbpAdmin.Collection.WithdrawAdmin", "AbpAdmin.Collection.Network.Admin", "SettingUi.ShowSettingPage"]);
            current["operations-payments"].GetAnyPermissionNames().ShouldContain("AbpAdmin.Collection.Collections");
            current["business"].IsHide.ShouldBeFalse();
            current["order-payments"].ParentId.ShouldBe(current["business"].Id);
            current["order-payments"].IsHide.ShouldBeTrue();
            current["financial-management"].Title.ShouldBe("资金与财务");
            current["administration"].OrderNo.ShouldBe(300);
            current["system"].OrderNo.ShouldBe(400);
            current["finance-overview"].PermissionName.ShouldBeNull();
            current["finance-overview"].GetAnyPermissionNames().ShouldContain("Finance.Rule.View");
            current["finance-rules"].IsHide.ShouldBeTrue();
            (await packages.WithDetailsAsync(x => x.Menus)).Single(x => x.Id == package.Id).Menus.Single().TemplateMenuId
                .ShouldBe(original["finance-rules"].Id);
        });
    }

    [Fact]
    public async Task V1_Upgrade_Preserves_Custom_Fields_And_Keeps_Custom_Trading_Descendants_In_Business()
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(1);
        var customChild = LegacyMenu(original["order-payments"].Id, MenuTypeEnum.Menu,
            "项目自己的业务", "custom-work", "/custom-work", 123);
        await WithUnitOfWorkAsync(async () =>
        {
            var finance = await _menuRepository.GetAsync(original["financial-management"].Id);
            finance.SetTitle("项目资金台");
            finance.SetIcon("customIcon");
            finance.SetOrderNo(2345);
            await _menuRepository.UpdateAsync(finance);
            await _menuRepository.InsertAsync(customChild);
        });

        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        var visible = await WithUnitOfWorkAsync(() => _myMenuAppService.GetAsync());
        visible.Items.ShouldNotContain(x => x.Path == "/order-payments" || x.Path == customChild.Path);
        var business = visible.Items.Single(x => x.Path == "/business");
        business.Children.Single(x => x.Path == "/order-payments").Children.ShouldContain(x => x.Path == customChild.Path);
        await WithUnitOfWorkAsync(async () =>
        {
            var finance = await _menuRepository.GetAsync(original["financial-management"].Id);
            finance.Title.ShouldBe("项目资金台");
            finance.Icon.ShouldBe("customIcon");
            finance.OrderNo.ShouldBe(2345);
            (await _menuRepository.GetAsync(customChild.Id)).ParentId.ShouldBe(original["order-payments"].Id);
        });
    }

    [Fact]
    public async Task V1_Upgrade_Does_Not_Reenable_Children_From_Disabled_Trading_Ancestor_Or_Move_Custom_Parents()
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(1);
        var customParent = LegacyMenu(null, MenuTypeEnum.Catalog, "自定义分组", "custom-group", "/custom-group", 150);
        await WithUnitOfWorkAsync(async () =>
        {
            await _menuRepository.InsertAsync(customParent);
            var trading = await _menuRepository.GetAsync(original["order-payments"].Id);
            trading.Disable();
            await _menuRepository.UpdateAsync(trading);
            var payments = await _menuRepository.GetAsync(original["operations-payments"].Id);
            payments.SetParent(customParent.Id);
            await _menuRepository.UpdateAsync(payments);
        });
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(async () =>
        {
            (await _menuRepository.GetAsync(original["collection-orders"].Id)).ParentId.ShouldBe(original["order-payments"].Id);
            (await _menuRepository.GetAsync(original["web3-operations"].Id)).ParentId.ShouldBe(original["order-payments"].Id);
            (await _menuRepository.GetAsync(original["operations-payments"].Id)).ParentId.ShouldBe(customParent.Id);
        });
        var visible = await WithUnitOfWorkAsync(() => _myMenuAppService.GetAsync());
        Flatten(visible.Items).ShouldNotContain(x => x.Path == "/collection/orders" || x.Path == "/web3/operations");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Legacy_Custom_Settings_Stay_In_Platform_While_Independent_Custom_Roots_Are_Preserved(int version)
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(version);
        var customSettings = LegacyMenu(original["configuration"].Id, MenuTypeEnum.Menu,
            "第三方模块参数", "custom-settings", "/custom-settings", 1500);
        var independent = LegacyMenu(null, MenuTypeEnum.Menu, "独立工作区", "independent", "/independent", 2000);
        await WithUnitOfWorkAsync(() => _menuRepository.InsertManyAsync([customSettings, independent]));
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        var visible = await WithUnitOfWorkAsync(() => _myMenuAppService.GetAsync());
        visible.Items.ShouldNotContain(x => x.Path == "/configuration" || x.Path == customSettings.Path);
        Flatten(visible.Items.Single(x => x.Path == "/administration").Children)
            .ShouldContain(x => x.Path == customSettings.Path);
        visible.Items.ShouldContain(x => x.Path == independent.Path);
        await WithUnitOfWorkAsync(async () =>
        {
            var compatibilityRoot = await _menuRepository.GetAsync(original["configuration"].Id);
            compatibilityRoot.ParentId.ShouldBe(original["administration"].Id);
            compatibilityRoot.IsHide.ShouldBe(version == 1);
            (await _menuRepository.GetAsync(customSettings.Id)).ParentId.ShouldBe(compatibilityRoot.Id);
            (await _menuRepository.GetAsync(independent.Id)).ParentId.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Explicit_Configuration_Package_Keeps_Only_The_Selected_Compatibility_Page()
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(1);
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(_currentTenant.Id!.Value,
                [original["finance-rules"].Id]));
            await WithUnitOfWorkAsync(async () =>
            {
                var menus = await _menuRepository.GetListAsync();
                var leaf = menus.Single(x => x.Type == MenuTypeEnum.Menu);
                leaf.Path.ShouldBe("/finance/rules");
                leaf.PermissionName.ShouldBe("Finance.Rule.View");
                leaf.IsHide.ShouldBeFalse();
                menus.ShouldNotContain(x => x.Path == "/finance/overview" || x.Path == "/finance/vouchers");
            });
        }
    }

    [Fact]
    public async Task V1_Tenant_Config_Snapshot_Keeps_Selected_Leaves_And_Their_Permissions()
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var original = await InsertPublishedLayoutAsync(1, ["financial-management", "finance", "finance-rules"]);
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));
            await WithUnitOfWorkAsync(async () =>
            {
                var menus = await _menuRepository.GetListAsync();
                menus.Count.ShouldBe(3);
                var rules = menus.Single(x => x.Type == MenuTypeEnum.Menu);
                rules.Id.ShouldBe(original["finance-rules"].Id);
                rules.IsHide.ShouldBeFalse();
                rules.PermissionName.ShouldBe("Finance.Rule.View");
                menus.Single(x => x.GetTemplateKey() == "financial-management").Title.ShouldBe("资金与财务");
            });
        }
    }

    [Fact]
    public async Task V3_Upgrade_Preserves_Custom_Workspace_And_Withdrawal_Fields()
    {
        await CleanMenusAsync();
        var original = await InsertPublishedLayoutAsync(3);
        var customParent = await CreateMenuAsync("自定义资金目录");
        await WithUnitOfWorkAsync(async () =>
        {
            var payment = await _menuRepository.GetAsync(original["operations-payments"].Id);
            payment.SetTitle("我的收款");
            payment.SetAnyPermissionNames(["Custom.Payments"]);
            var withdrawal = await _menuRepository.GetAsync(original["collection-withdrawals"].Id);
            withdrawal.SetParent(customParent.Id);
            withdrawal.SetTitle("我的提现");
            withdrawal.SetOrderNo(9876);
            withdrawal.SetPermissionName("Custom.Withdrawals");
        });
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        await WithUnitOfWorkAsync(async () =>
        {
            var payment = await _menuRepository.GetAsync(original["operations-payments"].Id);
            payment.Title.ShouldBe("我的收款");
            payment.GetAnyPermissionNames().ShouldBe(["Custom.Payments"]);
            var withdrawal = await _menuRepository.GetAsync(original["collection-withdrawals"].Id);
            withdrawal.ParentId.ShouldBe(customParent.Id);
            withdrawal.Title.ShouldBe("我的提现");
            withdrawal.OrderNo.ShouldBe(9876);
            withdrawal.PermissionName.ShouldBe("Custom.Withdrawals");
        });
    }

    [Fact]
    public async Task V3_Tenant_Upgrade_Does_Not_Add_Host_Leaves_Or_Expand_Selected_Permissions()
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var original = await InsertPublishedLayoutAsync(3, ["financial-management", "operations-payments"]);
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));
            await WithUnitOfWorkAsync(async () =>
            {
                var stored = await _menuRepository.GetListAsync();
                stored.Select(x => x.Id).OrderBy(x => x).ShouldBe(original.Values.Select(x => x.Id).OrderBy(x => x));
                stored.Single(x => x.GetTemplateKey() == "operations-payments").GetAnyPermissionNames()
                    .ShouldBe(["AbpAdmin.Collection.Payments.View", "SettingUi.ShowSettingPage"]);
            });
            var visible = await WithUnitOfWorkAsync(() => _myMenuAppService.GetAsync());
            Flatten(visible.Items).ShouldNotContain(x => x.Path == "/operations/payments" || x.Path == "/collection/withdrawals");
        }
    }

    private async Task<Dictionary<string, Menu>> InsertPublishedLayoutAsync(int version, string[]? selectedKeys = null)
    {
        var definitions = (version switch
            {
                0 => MenuTemplateDefinition.PreviousLayout,
                1 => MenuTemplateDefinition.Version1Layout,
                2 => MenuTemplateDefinition.Version2Layout,
                3 => MenuTemplateDefinition.Version3Layout,
                4 => MenuTemplateDefinition.Version4Layout,
                _ => throw new ArgumentOutOfRangeException(nameof(version))
            })
            .Where(x => x.Key != "biz-template-data-scope" && (selectedKeys == null || selectedKeys.Contains(x.Key))).ToArray();
        var ids = definitions.ToDictionary(x => x.Key, _ => _guidGenerator.Create());
        var menus = definitions.ToDictionary(x => x.Key, x =>
        {
            var menu = new Menu(ids[x.Key], _currentTenant.Id, x.ParentKey == null ? null : ids[x.ParentKey],
                x.Type, x.Title, x.Name, x.Path, x.Icon, x.OrderNo, x.IsHide, true, x.PermissionName);
            menu.SetAnyPermissionNames(x.AnyPermissionNames ?? []);
            if (version > 0) menu.SetTemplate(x.Key, version, x.HostOnly);
            return menu;
        });
        await WithUnitOfWorkAsync(() => _menuRepository.InsertManyAsync(menus.Values));
        return menus;
    }

    [Fact]
    public async Task V2_Tenant_Workspace_Upgrade_Fixes_Default_Subview_Permissions_Without_Adding_Leaves()
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var original = await InsertPublishedLayoutAsync(2,
                ["financial-management", "finance", "finance-vouchers", "administration", "configuration-integrations", "webhooks"]);
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));

            await WithUnitOfWorkAsync(async () =>
            {
                var current = (await _menuRepository.GetListAsync()).ToDictionary(x => x.GetTemplateKey()!);
                current.Keys.Order().ShouldBe(original.Keys.Order());
                foreach (var key in new[] { "finance-vouchers", "webhooks" })
                {
                    var expected = MenuTemplateDefinition.All.Single(x => x.Key == key);
                    current[key].Id.ShouldBe(original[key].Id);
                    current[key].PermissionName.ShouldBeNull();
                    current[key].GetAnyPermissionNames().ShouldBe(expected.AnyPermissionNames!);
                    current[key].GetTemplateVersion().ShouldBe(5);
                }
            });
            var service = MenuServiceWithPermissions(new HashSet<string> { "Finance.Adjust.Approve", "AbpAdmin.Webhooks.SendRecords" });
            var visible = await WithUnitOfWorkAsync(() => service.GetAsync());
            Flatten(visible.Items).Where(x => x.Children.Count == 0).Select(x => x.Path)
                .ShouldBe(["/finance/vouchers", "/administration/webhooks"]);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task V2_Workspace_Upgrade_Preserves_Either_Custom_Permission_Field(bool tenant, bool customAnyPermissions)
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(tenant ? Guid.NewGuid() : null))
        {
            var original = await InsertPublishedLayoutAsync(2,
                ["financial-management", "finance", "finance-vouchers", "administration", "configuration-integrations", "webhooks"]);
            await WithUnitOfWorkAsync(async () =>
            {
                foreach (var key in new[] { "finance-vouchers", "webhooks" })
                {
                    var menu = await _menuRepository.GetAsync(original[key].Id);
                    if (customAnyPermissions) menu.SetAnyPermissionNames(["Project.RestrictedWorkspace"]);
                    else menu.SetPermissionName("Project.RestrictedWorkspace");
                    await _menuRepository.UpdateAsync(menu);
                }
            });
            await WithUnitOfWorkAsync(() => tenant
                ? _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value)
                : _menuManager.SeedHostTemplateAsync());

            await WithUnitOfWorkAsync(async () =>
            {
                foreach (var key in new[] { "finance-vouchers", "webhooks" })
                {
                    var menu = await _menuRepository.GetAsync(original[key].Id);
                    var baseline = MenuTemplateDefinition.Version2Layout.Single(x => x.Key == key);
                    menu.PermissionName.ShouldBe(customAnyPermissions ? baseline.PermissionName : "Project.RestrictedWorkspace");
                    menu.GetAnyPermissionNames().ShouldBe(customAnyPermissions ? ["Project.RestrictedWorkspace"] : []);
                    menu.GetTemplateVersion().ShouldBe(5);
                }
            });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconcile_Requires_All_Scope_When_Agent_Scope_Is_Granted(bool allScope)
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        var permissions = new HashSet<string> { "Finance.Ledger.View", "Finance.Scope.Agent" };
        if (allScope) permissions.Add("Finance.Scope.All");
        var service = MenuServiceWithPermissions(permissions);
        var result = await WithUnitOfWorkAsync(() => service.GetAsync());
        Flatten(result.Items).Any(x => x.Path == "/finance/reconcile").ShouldBe(allScope);
        Flatten(result.Items).ShouldContain(x => x.Path == "/finance/overview");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tenant_Old_Overview_Uses_Its_Original_Ledger_Permission_And_Never_SettingsRead_Alone(bool ledgerPermission)
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await InsertPublishedLayoutAsync(1, ["financial-management", "finance", "finance-overview"]);
            await WithUnitOfWorkAsync(() => _menuManager.UpgradeTenantTemplateAsync(_currentTenant.Id!.Value));
            var permissions = new HashSet<string> { "SettingUi.ShowSettingPage" };
            if (ledgerPermission) permissions.Add("Finance.Ledger.View");
            var service = MenuServiceWithPermissions(permissions);
            var result = await WithUnitOfWorkAsync(() => service.GetAsync());
            Flatten(result.Items).Any(x => x.Path == "/finance/overview").ShouldBe(ledgerPermission);
            await WithUnitOfWorkAsync(async () =>
            {
                var overview = (await _menuRepository.GetListAsync()).Single(x => x.Path == "/finance/overview");
                overview.PermissionName.ShouldBe("Finance.Ledger.View");
                overview.GetAnyPermissionNames().ShouldBeEmpty();
            });
        }
    }

    private MyMenuAppService MenuServiceWithPermissions(ISet<string> grantedPermissions)
    {
        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string[]>()).Returns(call =>
        {
            var result = new MultiplePermissionGrantResult();
            foreach (var name in call.Arg<string[]>())
                result.Result[name] = grantedPermissions.Contains(name) ? PermissionGrantResult.Granted : PermissionGrantResult.Prohibited;
            return Task.FromResult(result);
        });
        return new MyMenuAppService(_menuRepository, _menuManager, checker)
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
        };
    }

    [Theory]
    [InlineData("finance-overview", "/finance/overview/")]
    [InlineData("finance-overview", "/finance/overview?settings=posting")]
    [InlineData("finance-overview", "/finance/overview#summary")]
    [InlineData("finance-reconcile", "/finance/reconcile/")]
    [InlineData("finance-reconcile", "/finance/reconcile?tab=overview")]
    [InlineData("finance-reconcile", "/finance/reconcile#summary")]
    public async Task BuiltIn_Finance_Constraints_Still_Apply_To_Equivalent_Deep_Links(string key, string path)
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var original = await InsertPublishedLayoutAsync(2, ["financial-management", "finance", key]);
            await WithUnitOfWorkAsync(async () =>
            {
                var menu = await _menuRepository.GetAsync(original[key].Id);
                menu.SetPath(path);
                await _menuRepository.UpdateAsync(menu);
            });
            var permissions = key == "finance-overview"
                ? new HashSet<string> { "SettingUi.ShowSettingPage" }
                : new HashSet<string> { "Finance.Ledger.View", "Finance.Scope.Agent" };
            var visible = await WithUnitOfWorkAsync(() => MenuServiceWithPermissions(permissions).GetAsync());
            Flatten(visible.Items).ShouldNotContain(x => x.Path == path);
        }
    }

    [Theory]
    [InlineData("finance-overview")]
    [InlineData("finance-reconcile")]
    public async Task Repurposed_Finance_Template_Node_Uses_Its_Custom_Route_And_Permission(string key)
    {
        await CleanMenusAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var original = await InsertPublishedLayoutAsync(2, ["financial-management", "finance", key]);
            await WithUnitOfWorkAsync(() => _menuAppService.UpdateAsync(original[key].Id, new MenuUpdateDto
            {
                Type = MenuTypeEnum.Menu,
                Title = "我的配置入口",
                ParentId = original["finance"].Id,
                Path = "/administration/identity/security-logs?settings=policy",
                PermissionName = "SettingUi.ShowSettingPage",
                IsEnabled = true
            }));
            var permissions = new HashSet<string> { "SettingUi.ShowSettingPage", "Finance.Scope.Agent" };
            var visible = await WithUnitOfWorkAsync(() => MenuServiceWithPermissions(permissions).GetAsync());
            Flatten(visible.Items).ShouldContain(x => x.Path == "/administration/identity/security-logs?settings=policy");
            var denied = await WithUnitOfWorkAsync(() => MenuServiceWithPermissions(new HashSet<string>()).GetAsync());
            Flatten(denied.Items).ShouldNotContain(x => x.Path == "/administration/identity/security-logs?settings=policy");
        }
    }

    [Theory]
    [InlineData("AbpAdmin.ModuleManagement")]
    [InlineData("AbpAdmin.ModuleManagement.Update")]
    public async Task Tenant_Module_Only_Role_Never_Receives_Host_Module_Management(string permission)
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var visible = await WithUnitOfWorkAsync(() => MenuServiceWithPermissions(new HashSet<string> { permission }).GetAsync());
            Flatten(visible.Items).Select(x => x.Path).ShouldBe(["/welcome"]);
        }
    }

    [Fact]
    public async Task Tenant_Settings_Only_Role_Receives_Only_Its_Usable_Configuration_Workspaces()
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var visible = await WithUnitOfWorkAsync(() => MenuServiceWithPermissions(
                new HashSet<string> { "SettingUi.ShowSettingPage" }).GetAsync());
            Flatten(visible.Items).Where(x => x.Children.Count == 0).Select(x => x.Path).ShouldBe(
                ["/welcome", "/administration/identity/security-logs", "/system/notifications", "/administration/audit-logs"]);
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

    [Theory]
    [InlineData("AbpAdmin.Collection.Collections", new[]
    {
        "/welcome", "/financial-management", "/operations/payments", "/user-funds", "/collection/member-funds", "/web3/operations"
    })]
    [InlineData("AbpAdmin.Collection.Payments.View", new[]
    {
        "/welcome", "/financial-management", "/operations/payments"
    })]
    [InlineData("AbpAdmin.Ordering.Orders", new[] { "/welcome", "/business", "/collection/orders" })]
    [InlineData("AbpAdmin.Ordering.Orders.ViewAll", new[] { "/welcome", "/business", "/collection/orders" })]
    [InlineData("SettingUi.ShowSettingPage", new[]
    {
        "/welcome", "/business", "/collection/orders", "/financial-management", "/operations/payments",
        "/user-funds", "/collection/withdrawals", "/web3/operations",
        "/finance", "/finance/overview", "/administration", "/administration/openiddict",
        "/administration/identity/security-logs", "/administration/saas", "/administration/saas/tenants",
        "/administration/notifications", "/system/notifications",
        "/system", "/system/audit", "/administration/audit-logs"
    })]
    [InlineData("AbpAdmin.Collection.Network.Admin", new[]
    {
        "/welcome", "/financial-management", "/operations/payments", "/user-funds", "/collection/withdrawals", "/web3/operations"
    })]
    [InlineData("AbpAdmin.Collection.WithdrawAdmin", new[]
    {
        "/welcome", "/financial-management", "/user-funds", "/collection/withdrawals", "/web3/operations"
    })]
    [InlineData("AbpAdmin.Collection.BalanceAdjustments.View", new[]
    {
        "/welcome", "/financial-management", "/user-funds", "/finance/balance-adjustments"
    })]
    [InlineData("Finance.BizDoc.View", new[] { "/welcome", "/financial-management", "/finance", "/finance/biz-docs" })]
    [InlineData("Finance.Voucher.View", new[] { "/welcome", "/financial-management", "/finance", "/finance/vouchers" })]
    [InlineData("Finance.Adjust.Create", new[] { "/welcome", "/financial-management", "/finance", "/finance/vouchers" })]
    [InlineData("Finance.Adjust.Submit", new[] { "/welcome", "/financial-management", "/finance", "/finance/vouchers" })]
    [InlineData("Finance.Adjust.Approve", new[] { "/welcome", "/financial-management", "/finance", "/finance/vouchers" })]
    [InlineData("Finance.Adjust.Reject", new[] { "/welcome", "/financial-management", "/finance", "/finance/vouchers" })]
    [InlineData("Finance.Adjust.Reverse", new[] { "/welcome", "/financial-management", "/finance", "/finance/vouchers" })]
    [InlineData("Finance.Rule.View", new[] { "/welcome", "/financial-management", "/finance", "/finance/overview" })]
    [InlineData("Finance.Period.View", new[] { "/welcome", "/financial-management", "/finance", "/finance/overview" })]
    [InlineData("Finance.Account.Manage", new[] { "/welcome", "/financial-management", "/finance", "/finance/overview" })]
    [InlineData("AbpAdmin.Languages", new[] { "/welcome", "/administration", "/administration/localization", "/administration/localization/languages" })]
    [InlineData("SettingManagement.TimeZone", new[] { "/welcome", "/administration", "/administration/localization", "/administration/localization/languages" })]
    [InlineData("SettingManagement.Emailing", new[] { "/welcome", "/administration", "/administration/notifications", "/system/notifications" })]
    [InlineData("AbpAdmin.Webhooks.Subscriptions", new[] { "/welcome", "/administration", "/configuration/integrations", "/administration/webhooks" })]
    [InlineData("AbpAdmin.Webhooks.SendRecords", new[] { "/welcome", "/administration", "/configuration/integrations", "/administration/webhooks" })]
    [InlineData("AbpAdmin.ModuleManagement", new[] { "/welcome", "/administration", "/administration/modules" })]
    [InlineData("No.Permissions.Granted", new[] { "/welcome" })]
    public async Task MyMenu_Shows_Only_Workspace_Entries_Available_To_The_Current_Role(
        string grantedPermission, string[] expectedPaths)
    {
        await CleanMenusAsync();
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());

        var checker = Substitute.For<IPermissionChecker>();
        checker.IsGrantedAsync(Arg.Any<string[]>()).Returns(call =>
        {
            var grants = new MultiplePermissionGrantResult();
            foreach (var name in call.Arg<string[]>())
            {
                grants.Result[name] = name == grantedPermission
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
        Flatten(myMenu.Items).Select(x => x.Path).ShouldBe(expectedPaths);
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
            paths.ShouldNotContain("/collection/orders");
            paths.ShouldNotContain("/administration/settings");
            paths.ShouldNotContain("/operations/payments");
            paths.ShouldNotContain("/configuration/payments");
            paths.ShouldNotContain("/collection/order-settings");
            paths.ShouldNotContain("/collection/settings");
            paths.ShouldNotContain("/web3/rules");
            paths.ShouldNotContain("/web3/operations");
            paths.ShouldNotContain("/web3/config");
            paths.ShouldNotContain("/finance/settings");
            paths.ShouldNotContain("/collection/member-funds");
            paths.ShouldNotContain("/collection/network");
            paths.ShouldNotContain("/alipay/settings");
            paths.ShouldNotContain("/wechat/settings");
            paths.ShouldNotContain("/stripe/settings");
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
