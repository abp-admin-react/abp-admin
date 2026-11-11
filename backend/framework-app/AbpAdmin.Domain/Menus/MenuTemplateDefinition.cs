using System.Collections.Generic;

namespace AbpAdmin.Menus;

/// <summary>
/// 全局菜单模板。目录按工作场景组织，页面沿用各模块的 ABP 权限。
/// </summary>
public static class MenuTemplateDefinition
{
    /// <summary>
    /// 父目录先于子项定义。路径是模板同步标识，移动菜单时保留原路径。
    /// 前端按菜单层级和名称翻译，缺少词条时使用模板标题。
    /// </summary>
    public static List<MenuDefinition> All => new()
    {
        new("welcome", null, MenuTypeEnum.Menu, "首页", "welcome", "/welcome", "smile", 10),

        new("administration", null, MenuTypeEnum.Catalog, "平台管理", "administration", "/administration", "appstore", 100),
        new("identity", "administration", MenuTypeEnum.Catalog, "身份管理", "identity", "/administration/identity", "team", 110),
        new("identity-users", "identity", MenuTypeEnum.Menu, "用户", "users", "/administration/identity/users", null, 111, "AbpIdentity.Users"),
        new("identity-roles", "identity", MenuTypeEnum.Menu, "角色", "roles", "/administration/identity/roles", null, 112, "AbpIdentity.Roles"),
        new("identity-organization-units", "identity", MenuTypeEnum.Menu, "部门", "organization-units", "/administration/identity/organization-units", null, 113, "AbpAdmin.OrganizationUnits"),
        new("identity-claim-types", "identity", MenuTypeEnum.Menu, "声明类型", "claim-types", "/administration/identity/claim-types", null, 114, "AbpAdmin.ClaimTypes"),

        new("saas", "administration", MenuTypeEnum.Catalog, "租户与功能", "saas", "/administration/saas", "cloud", 120),
        new("saas-tenants", "saas", MenuTypeEnum.Menu, "租户", "tenants", "/administration/saas/tenants", null, 121, "AbpTenantManagement.Tenants"),
        new("saas-editions", "saas", MenuTypeEnum.Menu, "版本", "editions", "/administration/saas/editions", null, 122, "AbpAdmin.Editions"),
        new("saas-tenant-packages", "saas", MenuTypeEnum.Menu, "租户套餐", "tenant-packages", "/administration/saas/tenant-packages", "cluster", 123, "AbpAdmin.TenantPackages"),
        new("features", "saas", MenuTypeEnum.Menu, "功能管理", "features", "/administration/features", "control", 124, "FeatureManagement.ManageHostFeatures"),

        new("localization", "administration", MenuTypeEnum.Catalog, "内容与语言", "localization", "/administration/localization", "global", 130),
        new("files", "localization", MenuTypeEnum.Menu, "文件管理", "files", "/administration/files", "folder", 131, "EasyAbp.FileManagement.File"),
        new("notifications", "localization", MenuTypeEnum.Menu, "通知", "notifications", "/system/notifications", "bell", 132, "EasyAbp.NotificationService.Notification.Manage"),
        new("text-templates", "localization", MenuTypeEnum.Menu, "文本模板", "text-templates", "/administration/text-templates", "fileText", 133, "AbpAdmin.TextTemplates"),
        new("localization-languages", "localization", MenuTypeEnum.Menu, "语言管理", "languages", "/administration/localization/languages", null, 134, "AbpAdmin.Languages"),
        new("localization-texts", "localization", MenuTypeEnum.Menu, "本地化文本", "language-texts", "/administration/localization/texts", null, 135, "AbpAdmin.LanguageTexts"),

        new("base-settings", "administration", MenuTypeEnum.Catalog, "基础设置", "base-settings", "/administration/base-settings", "setting", 140),
        new("settings", "base-settings", MenuTypeEnum.Menu, "系统设置", "settings", "/administration/settings", "setting", 141, "SettingUi.ShowSettingPage"),
        new("menus", "base-settings", MenuTypeEnum.Menu, "菜单管理", "menus", "/administration/menus", "menu", 142, "AbpAdmin.Menus"),
        new("data-dictionary", "base-settings", MenuTypeEnum.Menu, "数据字典", "data-dictionary", "/system/data-dictionary", "table", 143, "EasyAbp.Abp.DataDictionary.DataDictionary"),

        new("system", null, MenuTypeEnum.Catalog, "系统运维", "system", "/system", "desktop", 200),
        new("openiddict", "system", MenuTypeEnum.Catalog, "登录与接入", "openiddict", "/administration/openiddict", "safety", 210),
        new("identity-sessions", "openiddict", MenuTypeEnum.Menu, "登录会话", "sessions", "/administration/identity/sessions", null, 211, "AbpAdmin.Sessions"),
        new("identity-security-logs", "openiddict", MenuTypeEnum.Menu, "登录日志", "security-logs", "/administration/identity/security-logs", null, 212, "AbpAdmin.SecurityLogs"),
        new("openiddict-applications", "openiddict", MenuTypeEnum.Menu, "接入应用", "applications", "/administration/openiddict/applications", null, 213, "AbpAdmin.OpenIddict.Applications"),
        new("openiddict-scopes", "openiddict", MenuTypeEnum.Menu, "授权范围", "scopes", "/administration/openiddict/scopes", null, 214, "AbpAdmin.OpenIddict.Scopes"),
        new("openiddict-tokens", "openiddict", MenuTypeEnum.Menu, "令牌管理", "token-management", "/administration/openiddict/token-management", null, 215, "AbpAdmin.OpenIddict.Tokens"),

        new("audit", "system", MenuTypeEnum.Catalog, "审计与日志", "audit", "/system/audit", "fileSearch", 220),
        new("audit-logs", "audit", MenuTypeEnum.Menu, "审计日志", "audit-logs", "/administration/audit-logs", "fileSearch", 221, "AbpAdmin.AuditLogs"),
        new("operation-logs", "audit", MenuTypeEnum.Menu, "操作日志", "operation-logs", "/administration/operation-logs", "history", 222, "AbpAdmin.OperationLogs"),

        new("jobs", "system", MenuTypeEnum.Catalog, "作业调度", "jobs", "/system/jobs", "clockCircle", 230),
        new("background-jobs", "jobs", MenuTypeEnum.Menu, "后台作业", "background-jobs", "/system/background-jobs", "thunderbolt", 231, "AbpAdmin.BackgroundJobs"),
        new("scheduled-jobs", "jobs", MenuTypeEnum.Menu, "定时作业", "scheduled-jobs", "/system/scheduled-jobs", "clockCircle", 232, "AbpAdmin.ScheduledJobs"),

        new("monitoring", "system", MenuTypeEnum.Catalog, "运行监控", "monitoring", "/system/monitoring", "monitor", 240),
        new("server-monitor", "monitoring", MenuTypeEnum.Menu, "服务监控", "server-monitor", "/system/server-monitor", "dashboard", 241, "AbpAdmin.ServerMonitor"),
        new("cache-monitor", "monitoring", MenuTypeEnum.Menu, "缓存监控", "cache-monitor", "/system/cache-monitor", "hdd", 242, "AbpAdmin.CacheMonitor"),

        new("demos", "system", MenuTypeEnum.Catalog, "开发工具", "demos", "/demos", "experiment", 250),
        new("virtual-file-explorer", "demos", MenuTypeEnum.Menu, "虚拟文件浏览", "virtual-file-explorer", "/system/virtual-file-explorer", "folderOpen", 251, "AbpAdmin.VirtualFileExplorer"),
        new("biz-template-data-scope", "demos", MenuTypeEnum.Menu, "数据范围示例", "biz-template-data-scope", "/biz-template/data-scope", "apartment", 252, "BizTemplate.DataScopeDemo"),

        new("business", null, MenuTypeEnum.Catalog, "业务中心", "business", "/business", "fund", 300),
        // 收付款目录随已授权的子项显示，避免订单权限过滤目录并抬升其他业务入口。
        new("collection", "business", MenuTypeEnum.Catalog, "收付款", "collection", "/collection", "payCircle", 310),
        new("collection-dashboard", "collection", MenuTypeEnum.Menu, "资金看板", "collection-dashboard", "/collection/dashboard", "dashboard", 311, "AbpAdmin.Collection.Collections"),
        new("collection-orders", "collection", MenuTypeEnum.Menu, "订单", "collection-orders", "/collection/orders", "shoppingCart", 312, "AbpAdmin.Ordering.Orders"),
        new("payments", "collection", MenuTypeEnum.Menu, "支付记录", "payments", "/administration/payments", "payCircle", 313, "AbpAdmin.Payments"),
        new("collection-deposits", "collection", MenuTypeEnum.Menu, "充值单", "collection-deposits", "/collection/deposits", "wallet", 314, "AbpAdmin.Collection.Collections"),
        new("collection-withdrawals", "collection", MenuTypeEnum.Menu, "提现管理", "collection-withdrawals", "/collection/withdrawals", "moneyCollect", 315, "AbpAdmin.Collection.WithdrawAdmin"),
        new("collection-sweeps", "collection", MenuTypeEnum.Menu, "归集任务", "collection-sweeps", "/collection/sweeps", "send", 316, "AbpAdmin.Collection.Collections"),
        new("collection-vault", "collection", MenuTypeEnum.Menu, "金库核对", "collection-vault", "/collection/vault", "audit", 317, "AbpAdmin.Collection.Collections"),
        new("collection-member-funds", "collection", MenuTypeEnum.Menu, "会员资金", "collection-member-funds", "/collection/member-funds", "search", 318, "AbpAdmin.Collection.Collections"),
        new("collection-hot-wallet", "collection", MenuTypeEnum.Menu, "热钱包监控", "collection-hot-wallet", "/collection/hot-wallet", "monitor", 319, "AbpAdmin.Collection.WithdrawAdmin"),
        new("collection-network", "collection", MenuTypeEnum.Menu, "链网配置", "collection-network", "/collection/network", "setting", 320, "AbpAdmin.Collection.Network.Admin"),

        new("finance", "business", MenuTypeEnum.Catalog, "财务会计", "finance", "/finance", "calculator", 330, "Finance.Ledger.View"),
        new("finance-overview", "finance", MenuTypeEnum.Menu, "资金总览", "finance-overview", "/finance/overview", "fund", 331, "Finance.Ledger.View"),
        new("finance-accounts", "finance", MenuTypeEnum.Menu, "科目表", "finance-accounts", "/finance/accounts", "bank", 332, "Finance.Ledger.View"),
        new("finance-rules", "finance", MenuTypeEnum.Menu, "过账规则", "finance-rules", "/finance/rules", "profile", 333, "Finance.Ledger.View"),
        new("finance-vouchers", "finance", MenuTypeEnum.Menu, "凭证与调账", "finance-vouchers", "/finance/vouchers", "fileText", 334, "Finance.Ledger.View"),
        new("finance-period", "finance", MenuTypeEnum.Menu, "过账与期间", "finance-period", "/finance/period", "calendar", 335, "Finance.Ledger.View"),
        new("finance-reconcile", "finance", MenuTypeEnum.Menu, "对账", "finance-reconcile", "/finance/reconcile", "swap", 336, "Finance.Ledger.View"),
        new("finance-biz-docs", "finance", MenuTypeEnum.Menu, "业务单据", "finance-biz-docs", "/finance/biz-docs", "audit", 337, "Finance.Ledger.View"),
        new("finance-balances", "finance", MenuTypeEnum.Menu, "余额投影", "finance-balances", "/finance/balances", "table", 338, "Finance.Ledger.View"),
    };
}
