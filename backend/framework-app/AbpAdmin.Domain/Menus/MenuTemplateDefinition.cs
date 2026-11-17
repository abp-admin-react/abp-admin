using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace AbpAdmin.Menus;

/// <summary>
/// 全局菜单模板。目录按工作场景组织，页面沿用各模块的 ABP 权限。
/// </summary>
public static class MenuTemplateDefinition
{
    /// <summary>已知的宿主专属菜单路径，供租户已有菜单过滤。</summary>
    public static bool IsHostOnlyPath(string? path)
    {
        if (path == null) return false;
        var suffix = path.IndexOfAny(['?', '#']);
        var route = suffix >= 0 ? path[..suffix] : path;
        return HostOnlyPaths.Contains(route.TrimEnd('/'));
    }

    private static readonly HashSet<string> HostOnlyPaths = BuildHostOnlyPaths();
    private static readonly Dictionary<string, MenuDefinition> DefaultsByKey = All.ToDictionary(item => item.Key);

    public static bool UsesCustomTitle(Menu menu) =>
        menu.GetTemplateKey() is not { } key ||
        !DefaultsByKey.TryGetValue(key, out var definition) || menu.Title != definition.Title;

    private static HashSet<string> BuildHostOnlyPaths()
    {
        var keys = new HashSet<string>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var menu in All)
        {
            if (menu.HostOnly || menu.ParentKey is { } parent && keys.Contains(parent))
            {
                keys.Add(menu.Key);
                if (menu.Path is { } path)
                {
                    paths.Add(path);
                }
            }
        }
        return paths;
    }

    /// <summary>
    /// 当前默认布局。已发布节点保留 Key 和路径；旧叶节点继续为旧地址和套餐提供兼容入口。
    /// </summary>
    public static List<MenuDefinition> All => Build([]);

    internal static List<MenuDefinition> Build(IEnumerable<MenuDefinition> contributedMenus)
    {
        var adjusted = Version4Layout.Select(PlaceInVersion5Workspace).ToList();
        adjusted.AddRange(contributedMenus);
        var duplicate = adjusted.GroupBy(item => item.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw new AbpException($"Duplicate menu template key '{duplicate.Key}'.");
        }
        var duplicatePath = adjusted.Where(item => item.Path != null).GroupBy(item => item.Path)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePath != null)
        {
            throw new AbpException($"Duplicate menu template path '{duplicatePath.Key}'.");
        }

        var byParent = adjusted.ToLookup(item => item.ParentKey);
        var ordered = new List<MenuDefinition>(adjusted.Count);
        void AddChildren(string? key, bool hostOnly)
        {
            foreach (var child in byParent[key])
            {
                var resolved = hostOnly ? child with { HostOnly = true } : child;
                ordered.Add(resolved);
                AddChildren(resolved.Key, resolved.HostOnly);
            }
        }
        AddChildren(null, false);
        if (ordered.Count != adjusted.Count)
        {
            throw new AbpException("A menu template has an unknown parent or a cycle.");
        }
        return ordered;
    }

    private const string SettingsRead = "SettingUi.ShowSettingPage";

    internal static bool IsEmbeddedConfigurationMenu(string? key) => key is
        "settings" or "finance-accounts" or "finance-rules" or "finance-period" or "finance-settings";

    /// <summary>Version 3: existing workspaces expose each independently authorized subview.</summary>
    private static MenuDefinition AlignWorkspacePermissions(MenuDefinition item) => item.Key switch
    {
        "finance-vouchers" => item with
        {
            PermissionName = null,
            AnyPermissionNames = ["Finance.Voucher.View", "Finance.Adjust.Create", "Finance.Adjust.Submit",
                "Finance.Adjust.Approve", "Finance.Adjust.Reject", "Finance.Adjust.Reverse"]
        },
        "webhooks" => item with
        {
            PermissionName = null,
            AnyPermissionNames = ["AbpAdmin.Webhooks.Subscriptions", "AbpAdmin.Webhooks.SendRecords"]
        },
        _ => item
    };

    // Only these already-selected leaves may receive the v3 permission correction in a tenant snapshot.
    // This changes menu reachability, never ABP grants or the package's selected leaf set.
    internal static bool HasVersion3PermissionCorrection(string key) => key is "finance-vouchers" or "webhooks";

    /// <summary>Frozen published version 3, including the voucher/webhook permission corrections.</summary>
    internal static List<MenuDefinition> Version3Layout => Version2Layout.Select(AlignWorkspacePermissions).ToList();

    /// <summary>Frozen published version 4. Do not edit; version 5 migrates from this baseline.</summary>
    internal static List<MenuDefinition> Version4Layout => Version3Layout.Select(PlaceInVersion4Workspace).ToList();

    /// <summary>
    /// Version 5: the user-funds leaf is the balance-account list. Withdrawal review sits ahead of adjustment approval.
    /// Per-user activity stays on the same path with ?userId=.
    /// </summary>
    private static MenuDefinition PlaceInVersion5Workspace(MenuDefinition item) => item.Key switch
    {
        "collection-member-funds" => item with { Title = "用户余额" },
        "collection-withdrawals" => item with { OrderNo = 612 },
        "finance-balance-adjustments" => item with { OrderNo = 613 },
        _ => item
    };

    private static MenuDefinition PlaceInVersion4Workspace(MenuDefinition item) => item.Key switch
    {
        "operations-payments" => item with
        {
            Title = "收款管理",
            AnyPermissionNames = ["AbpAdmin.Collection.Payments.View", SettingsRead,
                "AbpAdmin.Collection.Collections", "AbpAdmin.Collection.Network.Admin"]
        },
        "user-funds" => item with { OrderNo = 220 },
        "web3-operations" => item with { Title = "钱包与链网", OrderNo = 230 },
        // Keep the existing leaf ID/path; configuration-only users can open its permitted settings pane.
        // The new parent is not Host-only, so the leaf carries the restriction explicitly.
        "collection-withdrawals" => item with
        {
            ParentKey = "user-funds", IsHide = false, OrderNo = 613, HostOnly = true,
            PermissionName = null,
            AnyPermissionNames = ["AbpAdmin.Collection.WithdrawAdmin", "AbpAdmin.Collection.Network.Admin", SettingsRead]
        },
        _ => item
    };

    /// <summary>Frozen published version 2; retained as the migration baseline for installed deployments.</summary>
    internal static List<MenuDefinition> Version2Layout => Version1Layout.Select(PlaceInVersion2Workspace).ToList();

    /// <summary>Version 2: task-oriented roots. Paths and keys remain compatible with existing packages and links.</summary>
    private static MenuDefinition PlaceInVersion2Workspace(MenuDefinition item) => item.Key switch
    {
        "welcome" => item with { OrderNo = 10 },
        "business" => item with { IsHide = false, OrderNo = 100 },
        "collection-orders" => item with { ParentKey = "business", OrderNo = 110 },
        // Keep custom descendants inside Business instead of lifting them to an extra root.
        "order-payments" => item with { ParentKey = "business", IsHide = true, OrderNo = 190 },
        "configuration" => item with { ParentKey = "administration", IsHide = true },
        "financial-management" => item with { Title = "资金与财务", OrderNo = 200 },
        "operations-payments" => item with { ParentKey = "financial-management", OrderNo = 210 },
        "web3-operations" => item with { ParentKey = "financial-management", OrderNo = 220 },
        "user-funds" => item with { OrderNo = 230 },
        "finance" => item with { OrderNo = 240 },
        "finance-overview" => item with
        {
            PermissionName = null,
            AnyPermissionNames = ["Finance.Ledger.View", "Finance.Account.Manage", "Finance.Rule.View", "Finance.Period.View", SettingsRead]
        },
        "finance-balances" => item with { Title = "账簿余额" },
        "administration" => item with { OrderNo = 300 },
        "system" => item with { OrderNo = 400 },
        "openiddict-tokens" => item with { Title = "令牌与授权" },
        "settings" or "finance-accounts" or "finance-rules" or "finance-period" or "finance-settings" => item with { IsHide = true },
        _ => item
    };

    /// <summary>
    /// Frozen published version 1. Migration compares each stored version with its actual baseline;
    /// changing this layout would mistake version-1 defaults for administrator customizations.
    /// </summary>
    internal static List<MenuDefinition> Version1Layout =>
    [
        .. PreviousLayout.Where(item => item.Key != "biz-template-data-scope").Select(PlaceInVersion1Workspace),
        new("notification-workspace", "administration", MenuTypeEnum.Catalog, "通知与模板",
            "notification-workspace", "/administration/notifications", "bell", 136),
        new("deployment-modules", "administration", MenuTypeEnum.Menu, "模块管理",
            "modules", "/administration/modules", "appstore", 148, "AbpAdmin.ModuleManagement", HostOnly: true)
    ];

    private static MenuDefinition PlaceInVersion1Workspace(MenuDefinition item) => item.Key switch
    {
        "configuration" => item with { IsHide = true },
        "configuration-payments" => item with { ParentKey = "order-payments", IsHide = true },
        "web3-configuration-page" => item with { ParentKey = "web3", IsHide = true },
        "finance-config" or "configuration-content" or "base-settings" => item with { IsHide = true },
        "configuration-integrations" => item with { ParentKey = "administration", OrderNo = 138 },
        "localization" => item with { Title = "语言与区域" },
        "localization-languages" => item with
        {
            ParentKey = "localization", OrderNo = 131, PermissionName = null,
            AnyPermissionNames = ["AbpAdmin.Languages", "SettingManagement.TimeZone"]
        },
        "localization-texts" => item with { ParentKey = "localization", OrderNo = 132 },
        "text-templates" => item with { ParentKey = "notification-workspace", OrderNo = 138 },
        "notifications" => item with
        {
            ParentKey = "notification-workspace", Title = "通知管理", OrderNo = 137, PermissionName = null,
            AnyPermissionNames = ["EasyAbp.NotificationService.Notification.Manage", "SettingManagement.Emailing", SettingsRead]
        },
        "files" or "menus" or "data-dictionary" or "settings" => item with
        {
            ParentKey = "administration", Title = item.Key == "settings" ? "扩展配置" : item.Title,
            OrderNo = item.Key == "files" ? 140 : item.OrderNo
        },
        "features" => item with { ParentKey = "saas", Title = "宿主功能配置", HostOnly = true },
        "saas-tenants" => item with
        {
            PermissionName = null, AnyPermissionNames = ["AbpTenantManagement.Tenants", SettingsRead], HostOnly = true
        },
        "openiddict" => item with { ParentKey = "administration", Title = "登录与安全", OrderNo = 115 },
        "openiddict-tokens" => item with { ParentKey = "configuration-integrations", OrderNo = 1064 },
        "identity-security-logs" => item with
        {
            PermissionName = null, AnyPermissionNames = ["AbpAdmin.SecurityLogs", SettingsRead]
        },
        "audit-logs" => item with
        {
            PermissionName = null, AnyPermissionNames = ["AbpAdmin.AuditLogs", SettingsRead]
        },
        "collection-orders" => item with
        {
            AnyPermissionNames = ["AbpAdmin.Ordering.Orders", "AbpAdmin.Ordering.Orders.ViewAll", SettingsRead]
        },
        "operations-payments" => item with
        {
            PermissionName = null, AnyPermissionNames = ["AbpAdmin.Collection.Payments.View", SettingsRead]
        },
        "web3-operations" => item with
        {
            AnyPermissionNames = ["AbpAdmin.Collection.Collections", "AbpAdmin.Collection.WithdrawAdmin",
                "AbpAdmin.Collection.Network.Admin", SettingsRead]
        },
        "finance-accounts" or "finance-rules" or "finance-period" or "finance-settings" => item with { ParentKey = "finance" },
        _ => item
    };

    /// <summary>发布前的模板，用于辨别旧默认字段和管理员定制。升级后也要保留这份基线。</summary>
    internal static List<MenuDefinition> PreviousLayout => new()
    {
        new("welcome", null, MenuTypeEnum.Menu, "工作台", "welcome", "/welcome", "smile", 10),

        new("configuration", null, MenuTypeEnum.Catalog, "设置中心", "configuration", "/configuration", "setting", 1000),
        new("configuration-payments", "configuration", MenuTypeEnum.Menu, "订单与收款", "configuration-payments", "/configuration/payments", "payCircle", 1010,
            "SettingUi.ShowSettingPage", HostOnly: true),
        new("web3-configuration-page", "configuration", MenuTypeEnum.Menu, "链上充提", "web3-configuration-page", "/web3/config", "wallet", 1020,
            HostOnly: true, AnyPermissionNames: ["AbpAdmin.Collection.Network.Admin", "SettingUi.ShowSettingPage", "AbpAdmin.Collection.WithdrawAdmin"]),
        new("finance-config", "configuration", MenuTypeEnum.Catalog, "会计设置", "finance-config", "/finance/configuration", "calculator", 1030),
        new("base-settings", "configuration", MenuTypeEnum.Catalog, "平台设置", "base-settings", "/administration/base-settings", "setting", 1040),
        new("configuration-content", "configuration", MenuTypeEnum.Catalog, "语言与模板", "configuration-content", "/configuration/content", "global", 1050),
        new("configuration-integrations", "configuration", MenuTypeEnum.Catalog, "应用接入", "configuration-integrations", "/configuration/integrations", "api", 1060),

        new("administration", null, MenuTypeEnum.Catalog, "平台管理", "administration", "/administration", "appstore", 800),
        new("identity", "administration", MenuTypeEnum.Catalog, "用户与权限", "identity", "/administration/identity", "team", 110),
        new("identity-users", "identity", MenuTypeEnum.Menu, "用户", "users", "/administration/identity/users", null, 111, "AbpIdentity.Users"),
        new("identity-roles", "identity", MenuTypeEnum.Menu, "角色", "roles", "/administration/identity/roles", null, 112, "AbpIdentity.Roles"),
        new("identity-organization-units", "identity", MenuTypeEnum.Menu, "部门", "organization-units", "/administration/identity/organization-units", null, 113, "AbpAdmin.OrganizationUnits"),
        new("identity-claim-types", "identity", MenuTypeEnum.Menu, "用户身份属性", "claim-types", "/administration/identity/claim-types", null, 114, "AbpAdmin.ClaimTypes"),

        new("saas", "administration", MenuTypeEnum.Catalog, "租户管理", "saas", "/administration/saas", "cloud", 120),
        new("saas-tenants", "saas", MenuTypeEnum.Menu, "租户", "tenants", "/administration/saas/tenants", null, 121, "AbpTenantManagement.Tenants"),
        new("saas-editions", "saas", MenuTypeEnum.Menu, "租户功能版本", "editions", "/administration/saas/editions", null, 122, "AbpAdmin.Editions"),
        new("saas-tenant-packages", "saas", MenuTypeEnum.Menu, "租户菜单套餐", "tenant-packages", "/administration/saas/tenant-packages", "cluster", 123, "AbpAdmin.TenantPackages"),

        new("localization", "administration", MenuTypeEnum.Catalog, "内容与通知", "localization", "/administration/localization", "global", 130),
        new("files", "localization", MenuTypeEnum.Menu, "文件管理", "files", "/administration/files", "folder", 131, "EasyAbp.FileManagement.File"),
        new("notifications", "localization", MenuTypeEnum.Menu, "通知发送记录", "notifications", "/system/notifications", "bell", 132, "EasyAbp.NotificationService.Notification.Manage"),
        new("localization-languages", "configuration-content", MenuTypeEnum.Menu, "语言管理", "languages", "/administration/localization/languages", null, 1051, "AbpAdmin.Languages"),
        new("localization-texts", "configuration-content", MenuTypeEnum.Menu, "界面翻译", "language-texts", "/administration/localization/texts", null, 1052, "AbpAdmin.LanguageTexts"),
        new("text-templates", "configuration-content", MenuTypeEnum.Menu, "文本模板", "text-templates", "/administration/text-templates", "fileText", 1053, "AbpAdmin.TextTemplates"),

        new("settings", "base-settings", MenuTypeEnum.Menu, "通用设置", "settings", "/administration/settings", "setting", 141, "SettingUi.ShowSettingPage"),
        new("menus", "base-settings", MenuTypeEnum.Menu, "后台菜单管理", "menus", "/administration/menus", "menu", 142, "AbpAdmin.Menus"),
        new("data-dictionary", "base-settings", MenuTypeEnum.Menu, "数据字典", "data-dictionary", "/system/data-dictionary", "table", 143, "EasyAbp.Abp.DataDictionary.DataDictionary"),
        new("features", "base-settings", MenuTypeEnum.Menu, "平台功能开关", "features", "/administration/features", "control", 144, "FeatureManagement.ManageHostFeatures"),

        new("system", null, MenuTypeEnum.Catalog, "系统运维", "system", "/system", "desktop", 900),
        new("openiddict", "system", MenuTypeEnum.Catalog, "登录与令牌", "openiddict", "/administration/openiddict", "safety", 210),
        new("identity-sessions", "openiddict", MenuTypeEnum.Menu, "登录会话", "sessions", "/administration/identity/sessions", null, 211, "AbpAdmin.Sessions"),
        new("identity-security-logs", "openiddict", MenuTypeEnum.Menu, "登录安全日志", "security-logs", "/administration/identity/security-logs", null, 212, "AbpAdmin.SecurityLogs"),
        new("openiddict-applications", "configuration-integrations", MenuTypeEnum.Menu, "接入应用", "applications", "/administration/openiddict/applications", null, 1061, "AbpAdmin.OpenIddict.Applications"),
        new("openiddict-scopes", "configuration-integrations", MenuTypeEnum.Menu, "授权范围", "scopes", "/administration/openiddict/scopes", null, 1062, "AbpAdmin.OpenIddict.Scopes"),
        new("openiddict-tokens", "openiddict", MenuTypeEnum.Menu, "令牌管理", "token-management", "/administration/openiddict/token-management", null, 215, "AbpAdmin.OpenIddict.Tokens"),
        new("webhooks", "configuration-integrations", MenuTypeEnum.Menu, "事件回调订阅", "webhooks", "/administration/webhooks", null, 1063, "AbpAdmin.Webhooks.Subscriptions"),

        new("audit", "system", MenuTypeEnum.Catalog, "审计与日志", "audit", "/system/audit", "fileSearch", 220),
        new("audit-logs", "audit", MenuTypeEnum.Menu, "接口审计日志", "audit-logs", "/administration/audit-logs", "fileSearch", 221, "AbpAdmin.AuditLogs"),
        new("operation-logs", "audit", MenuTypeEnum.Menu, "业务操作日志", "operation-logs", "/administration/operation-logs", "history", 222, "AbpAdmin.OperationLogs"),

        new("jobs", "system", MenuTypeEnum.Catalog, "任务调度", "jobs", "/system/jobs", "clockCircle", 230),
        new("background-jobs", "jobs", MenuTypeEnum.Menu, "后台任务", "background-jobs", "/system/background-jobs", "thunderbolt", 231, "AbpAdmin.BackgroundJobs"),
        new("scheduled-jobs", "jobs", MenuTypeEnum.Menu, "定时任务", "scheduled-jobs", "/system/scheduled-jobs", "clockCircle", 232, "AbpAdmin.ScheduledJobs"),

        new("monitoring", "system", MenuTypeEnum.Catalog, "运行监控", "monitoring", "/system/monitoring", "monitor", 240),
        new("server-monitor", "monitoring", MenuTypeEnum.Menu, "服务监控", "server-monitor", "/system/server-monitor", "dashboard", 241, "AbpAdmin.ServerMonitor"),
        new("cache-monitor", "monitoring", MenuTypeEnum.Menu, "缓存监控", "cache-monitor", "/system/cache-monitor", "hdd", 242, "AbpAdmin.CacheMonitor"),

        new("demos", "system", MenuTypeEnum.Catalog, "开发工具", "demos", "/demos", "experiment", 250),
        new("virtual-file-explorer", "demos", MenuTypeEnum.Menu, "虚拟文件浏览", "virtual-file-explorer", "/system/virtual-file-explorer", "folderOpen", 251, "AbpAdmin.VirtualFileExplorer"),
        new("biz-template-data-scope", "demos", MenuTypeEnum.Menu, "数据权限示例", "biz-template-data-scope", "/biz-template/data-scope", "apartment", 252, "BizTemplate.DataScopeDemo", IsHide: true),

        // 隐藏旧目录时保留路径和 ID，已有套餐引用及自定义子项仍可解析。
        new("business", null, MenuTypeEnum.Catalog, "业务中心", "business", "/business", "fund", 700, IsHide: true),
        new("order-payments", null, MenuTypeEnum.Catalog, "交易运营", "order-payments", "/order-payments", "shoppingCart", 100),
        new("collection-orders", "order-payments", MenuTypeEnum.Menu, "订单管理", "collection-orders", "/collection/orders", "shoppingCart", 101,
            HostOnly: true, AnyPermissionNames: ["AbpAdmin.Ordering.Orders", "AbpAdmin.Ordering.Orders.ViewAll"]),
        new("operations-payments", "order-payments", MenuTypeEnum.Menu, "收款与退款", "operations-payments", "/operations/payments", "payCircle", 102,
            "AbpAdmin.Collection.Payments.View", HostOnly: true),
        new("web3-operations", "order-payments", MenuTypeEnum.Menu, "链上充提", "web3-operations", "/web3/operations", "wallet", 103,
            HostOnly: true, AnyPermissionNames: ["AbpAdmin.Collection.Collections", "AbpAdmin.Collection.WithdrawAdmin", "AbpAdmin.Collection.Network.Admin"]),
        new("collection", "order-payments", MenuTypeEnum.Catalog, "订单", "collection", "/collection", "shoppingCart", 110, IsHide: true),
        new("order-settings", "collection", MenuTypeEnum.Menu, "订单与报价设置", "collection-order-settings", "/collection/order-settings", null, 112,
            "SettingUi.ShowSettingPage", IsHide: true, HostOnly: true),
        new("collection-settings", "collection", MenuTypeEnum.Menu, "余额支付设置", "collection-settings", "/collection/settings", null, 113,
            "SettingUi.ShowSettingPage", IsHide: true, HostOnly: true),
        new("payments", "collection", MenuTypeEnum.Menu, "跨渠道收款与退款", "payments", "/administration/payments", "payCircle", 114,
            "AbpAdmin.Collection.Payments.View", IsHide: true, HostOnly: true),

        new("web3", "order-payments", MenuTypeEnum.Catalog, "Web3 充值提现", "web3", "/web3", "wallet", 200, IsHide: true, HostOnly: true),
        // 历史页面路径与菜单 ID 继续保留，正式侧栏从交易运营、设置中心进入。
        new("collection-deposits", "web3", MenuTypeEnum.Menu, "充值记录", "collection-deposits", "/collection/deposits", null, 211, "AbpAdmin.Collection.Collections", IsHide: true),
        new("collection-withdrawals", "web3", MenuTypeEnum.Menu, "提现审核与打款", "collection-withdrawals", "/collection/withdrawals", null, 212, "AbpAdmin.Collection.WithdrawAdmin", IsHide: true),
        new("collection-sweeps", "web3", MenuTypeEnum.Menu, "充值归集任务", "collection-sweeps", "/collection/sweeps", null, 213, "AbpAdmin.Collection.Collections", IsHide: true),
        new("collection-dashboard", "web3", MenuTypeEnum.Menu, "链上监控", "collection-dashboard", "/collection/dashboard", null, 214, "AbpAdmin.Collection.Collections", IsHide: true),
        new("web3-rules", "web3", MenuTypeEnum.Menu, "充值与提现规则", "web3-rules", "/web3/rules", null, 215,
            IsHide: true, AnyPermissionNames: ["AbpAdmin.Collection.Network.Admin", "SettingUi.ShowSettingPage"]),
        new("collection-network", "web3", MenuTypeEnum.Menu, "链网络设置", "collection-network", "/collection/network", null, 216,
            "AbpAdmin.Collection.Network.Admin", IsHide: true),
        new("collection-wallets", "web3", MenuTypeEnum.Menu, "链上钱包设置", "collection-wallets", "/collection/wallets", null, 217,
            IsHide: true, AnyPermissionNames: ["AbpAdmin.Collection.Network.Admin", "AbpAdmin.Collection.WithdrawAdmin"]),
        new("collection-vault", "web3", MenuTypeEnum.Menu, "储备余额核对", "collection-vault", "/collection/vault", null, 218,
            "AbpAdmin.Collection.Collections", IsHide: true),
        new("collection-hot-wallet", "web3", MenuTypeEnum.Menu, "提现钱包监控", "collection-hot-wallet", "/collection/hot-wallet", null, 219,
            "AbpAdmin.Collection.WithdrawAdmin", IsHide: true),
        new("web3-config", "web3", MenuTypeEnum.Catalog, "充提配置", "web3-config", "/web3/configuration", "setting", 220, IsHide: true),
        new("web3-settings", "web3-config", MenuTypeEnum.Menu, "链上运行规则", "web3-settings", "/web3/settings", null, 221,
            "SettingUi.ShowSettingPage", IsHide: true),

        new("alipay", "order-payments", MenuTypeEnum.Catalog, "支付宝", "alipay", "/alipay", "alipayCircle", 300, IsHide: true, HostOnly: true),
        new("alipay-payments", "alipay", MenuTypeEnum.Menu, "收款与退款", "alipay-payments", "/alipay/payments", null, 311, "AbpAdmin.Collection.Payments.View", IsHide: true),
        new("alipay-settings", "alipay", MenuTypeEnum.Menu, "收款设置", "alipay-settings", "/alipay/settings", null, 312, "SettingUi.ShowSettingPage", IsHide: true),

        new("wechat", "order-payments", MenuTypeEnum.Catalog, "微信支付", "wechat", "/wechat", "wechat", 400, IsHide: true, HostOnly: true),
        new("wechat-payments", "wechat", MenuTypeEnum.Menu, "收款与退款", "wechat-payments", "/wechat/payments", null, 411, "AbpAdmin.Collection.Payments.View", IsHide: true),
        new("wechat-settings", "wechat", MenuTypeEnum.Menu, "收款设置", "wechat-settings", "/wechat/settings", null, 412, "SettingUi.ShowSettingPage", IsHide: true),

        new("stripe", "order-payments", MenuTypeEnum.Catalog, "Stripe", "stripe", "/stripe", "creditCard", 500, IsHide: true, HostOnly: true),
        new("stripe-payments", "stripe", MenuTypeEnum.Menu, "收款与退款", "stripe-payments", "/stripe/payments", null, 511, "AbpAdmin.Collection.Payments.View", IsHide: true),
        new("stripe-settings", "stripe", MenuTypeEnum.Menu, "收款设置", "stripe-settings", "/stripe/settings", null, 512, "SettingUi.ShowSettingPage", IsHide: true),

        new("financial-management", null, MenuTypeEnum.Catalog, "财务管理", "financial-management", "/financial-management", "calculator", 600),
        new("user-funds", "financial-management", MenuTypeEnum.Catalog, "用户资金", "user-funds", "/user-funds", "fund", 610),
        new("collection-member-funds", "user-funds", MenuTypeEnum.Menu, "用户余额与流水", "collection-member-funds", "/collection/member-funds", null, 611,
            "AbpAdmin.Collection.Collections", HostOnly: true),
        new("finance-balance-adjustments", "user-funds", MenuTypeEnum.Menu, "余额调整审批", "finance-balance-adjustments", "/finance/balance-adjustments", null, 612,
            "AbpAdmin.Collection.BalanceAdjustments.View", HostOnly: true),

        new("finance", "financial-management", MenuTypeEnum.Catalog, "会计与对账", "finance", "/finance", "calculator", 650),
        new("finance-overview", "finance", MenuTypeEnum.Menu, "会计概览", "finance-overview", "/finance/overview", null, 651, "Finance.Ledger.View"),
        new("finance-vouchers", "finance", MenuTypeEnum.Menu, "会计凭证", "finance-vouchers", "/finance/vouchers", null, 652, "Finance.Voucher.View"),
        new("finance-reconcile", "finance", MenuTypeEnum.Menu, "账务对账", "finance-reconcile", "/finance/reconcile", null, 653, "Finance.Ledger.View"),
        new("finance-balances", "finance", MenuTypeEnum.Menu, "用户余额核对", "finance-balances", "/finance/balances", null, 654, "Finance.Balance.View"),
        new("finance-biz-docs", "finance", MenuTypeEnum.Menu, "凭证来源查询", "finance-biz-docs", "/finance/biz-docs", null, 655, "Finance.BizDoc.View"),
        new("finance-transactions", "finance", MenuTypeEnum.Menu, "分录查询", "finance-transactions", "/finance/transactions", null, 656,
            "Finance.Voucher.View", IsHide: true),
        new("finance-accounts", "finance-config", MenuTypeEnum.Menu, "会计科目", "finance-accounts", "/finance/accounts", null, 661,
            AnyPermissionNames: ["Finance.Ledger.View", "Finance.Account.Manage"]),
        new("finance-rules", "finance-config", MenuTypeEnum.Menu, "自动记账规则", "finance-rules", "/finance/rules", null, 662, "Finance.Rule.View"),
        new("finance-period", "finance-config", MenuTypeEnum.Menu, "会计期间", "finance-period", "/finance/period", null, 663, "Finance.Period.View"),
        new("finance-settings", "finance-config", MenuTypeEnum.Menu, "记账设置", "finance-settings", "/finance/settings", null, 664,
            "SettingUi.ShowSettingPage", HostOnly: true),
    };
}
