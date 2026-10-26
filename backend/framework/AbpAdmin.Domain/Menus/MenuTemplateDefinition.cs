using System.Collections.Generic;

namespace AbpAdmin.Menus;

/// <summary>
/// 全局菜单模板的节点定义（纯数据，与 Domain 行为分离）。
/// <para>层级对齐 ABP Pro 官方口径：Dashboard/首页打头 → 业务域（收付款、财务）
/// → 管理治理（身份/审计/设置收尾）→ SaaS 独立顶层组 → 系统运维 → 示例演示置底。
/// OrderNo 全树同一比较空间（父子紧邻：组 n、子项 n+1..n+k），my-menu 按其升序渲染。</para>
/// <para>权限绑定与 src/access.ts 的 grantedPolicies 映射一致。Domain 层不引用
/// Application.Contracts，权限名用字面量（与各模块 PermissionDefinitionProvider
/// 注册名一致，稳定不变）。菜单可见性只是导航引导（粗粒度），页面与接口才是真权限面。</para>
/// <para>Title 是「locale key 缺失时的中文兜底文案」（国际化走前端 menu.{parent}.{name}
/// 语言包），不是正规文案出处——英文环境首访兜底生效时菜单显示中文属预期行为。</para>
/// </summary>
public static class MenuTemplateDefinition
{
    /// <summary>
    /// 全局模板树。新增页面时在 routes.ts 加路由、在前端语言包加 menu.* key，并在这里补一个节点。
    /// SeedHostTemplateAsync 会把模板同步到已有 Host 库（改父/改序/改题/改图标/改权限），
    /// 模板中删除的节点不会从库中删除（保留管理员在菜单管理页的自定义添加项）。
    /// </summary>
    public static List<MenuDefinition> All => new()
    {
        // ===== 首页（ABP Pro 的 Dashboard 位） =====
        new("welcome", null, MenuTypeEnum.Menu, "首页", "welcome", "/welcome", "smile", 10),

        // ===== 收付款域（Ordering + Collection）：核心业务紧跟首页 =====
        // 父组权限位单值口径（MenuDefinition 不支持 OR）：绑订单权限位——只有收付款
        // 子域权限的角色看不到父组（子页仍可直达 URL）；前端 access.ts 是 OR 口径。
        new("collection", null, MenuTypeEnum.Catalog, "收付款", "collection", "/collection", "payCircle", 20, "AbpAdmin.Ordering.Orders"),
        // 子项按资金流排：总览 → 业务入口 → 入金 → 出金审核 → 链上搬运 → 对账 → 查询 → 风控 → 高危运维殿后
        new("collection-dashboard", "collection", MenuTypeEnum.Menu, "资金看板", "collection-dashboard", "/collection/dashboard", "dashboard", 21, "AbpAdmin.Collection.Collections"),
        new("collection-orders", "collection", MenuTypeEnum.Menu, "订单", "collection-orders", "/collection/orders", "shoppingCart", 22, "AbpAdmin.Ordering.Orders"),
        new("collection-deposits", "collection", MenuTypeEnum.Menu, "充值单", "collection-deposits", "/collection/deposits", "wallet", 23, "AbpAdmin.Collection.Collections"),
        new("collection-withdrawals", "collection", MenuTypeEnum.Menu, "提现管理", "collection-withdrawals", "/collection/withdrawals", "moneyCollect", 24, "AbpAdmin.Collection.WithdrawAdmin"),
        new("collection-sweeps", "collection", MenuTypeEnum.Menu, "归集任务", "collection-sweeps", "/collection/sweeps", "send", 25, "AbpAdmin.Collection.Collections"),
        new("collection-vault", "collection", MenuTypeEnum.Menu, "金库核对", "collection-vault", "/collection/vault", "audit", 26, "AbpAdmin.Collection.Collections"),
        new("collection-member-funds", "collection", MenuTypeEnum.Menu, "会员资金", "collection-member-funds", "/collection/member-funds", "search", 27, "AbpAdmin.Collection.Collections"),
        new("collection-hot-wallet", "collection", MenuTypeEnum.Menu, "热钱包监控", "collection-hot-wallet", "/collection/hot-wallet", "monitor", 28, "AbpAdmin.Collection.WithdrawAdmin"),
        new("collection-network", "collection", MenuTypeEnum.Menu, "链网配置", "collection-network", "/collection/network", "setting", 29, "AbpAdmin.Collection.Network.Admin"),

        // ===== 财务域（AbpAdmin.Ledger 基础财务层 A1–A7，此前只有路由未挂菜单） =====
        // 菜单可见性统一粗粒度绑 Finance.Ledger.View（引导面）；页面/按钮各有细粒度权限。
        new("finance", null, MenuTypeEnum.Catalog, "财务会计", "finance", "/finance", "calculator", 30, "Finance.Ledger.View"),
        new("finance-overview", "finance", MenuTypeEnum.Menu, "资金总览", "finance-overview", "/finance/overview", "fund", 31, "Finance.Ledger.View"),
        new("finance-accounts", "finance", MenuTypeEnum.Menu, "科目表", "finance-accounts", "/finance/accounts", "bank", 32, "Finance.Ledger.View"),
        new("finance-rules", "finance", MenuTypeEnum.Menu, "过账规则", "finance-rules", "/finance/rules", "profile", 33, "Finance.Ledger.View"),
        new("finance-vouchers", "finance", MenuTypeEnum.Menu, "凭证与调账", "finance-vouchers", "/finance/vouchers", "fileText", 34, "Finance.Ledger.View"),
        new("finance-period", "finance", MenuTypeEnum.Menu, "过账与期间", "finance-period", "/finance/period", "calendar", 35, "Finance.Ledger.View"),
        new("finance-reconcile", "finance", MenuTypeEnum.Menu, "对账", "finance-reconcile", "/finance/reconcile", "swap", 36, "Finance.Ledger.View"),
        new("finance-biz-docs", "finance", MenuTypeEnum.Menu, "业务单据", "finance-biz-docs", "/finance/biz-docs", "audit", 37, "Finance.Ledger.View"),
        new("finance-balances", "finance", MenuTypeEnum.Menu, "余额投影", "finance-balances", "/finance/balances", "table", 38, "Finance.Ledger.View"),

        // ===== 管理（ABP Pro 的 Administration 位：身份治理 → 审计 → 设置收尾） =====
        new("administration", null, MenuTypeEnum.Catalog, "管理", "administration", "/administration", "appstore", 40),
        new("identity", "administration", MenuTypeEnum.Catalog, "身份管理", "identity", "/administration/identity", "team", 41),
        new("identity-users", "identity", MenuTypeEnum.Menu, "用户", "users", "/administration/identity/users", null, 41, "AbpIdentity.Users"),
        new("identity-roles", "identity", MenuTypeEnum.Menu, "角色", "roles", "/administration/identity/roles", null, 42, "AbpIdentity.Roles"),
        new("identity-organization-units", "identity", MenuTypeEnum.Menu, "组织单元", "organization-units", "/administration/identity/organization-units", null, 43, "AbpAdmin.OrganizationUnits"),
        new("identity-posts", "identity", MenuTypeEnum.Menu, "岗位", "posts", "/administration/identity/posts", null, 44, "AbpAdmin.Posts"),
        new("identity-claim-types", "identity", MenuTypeEnum.Menu, "声明类型", "claim-types", "/administration/identity/claim-types", null, 45, "AbpAdmin.ClaimTypes"),
        new("identity-security-logs", "identity", MenuTypeEnum.Menu, "安全日志", "security-logs", "/administration/identity/security-logs", null, 46, "AbpAdmin.SecurityLogs"),
        new("identity-sessions", "identity", MenuTypeEnum.Menu, "会话", "sessions", "/administration/identity/sessions", null, 47, "AbpAdmin.Sessions"),
        new("openiddict", "administration", MenuTypeEnum.Catalog, "OpenIddict", "openiddict", "/administration/openiddict", "safety", 42),
        new("openiddict-applications", "openiddict", MenuTypeEnum.Menu, "应用程序", "applications", "/administration/openiddict/applications", null, 42, "AbpAdmin.OpenIddict.Applications"),
        new("openiddict-scopes", "openiddict", MenuTypeEnum.Menu, "范围", "scopes", "/administration/openiddict/scopes", null, 43, "AbpAdmin.OpenIddict.Scopes"),
        new("openiddict-tokens", "openiddict", MenuTypeEnum.Menu, "令牌管理", "token-management", "/administration/openiddict/token-management", null, 44, "AbpAdmin.OpenIddict.Tokens"),
        new("features", "administration", MenuTypeEnum.Menu, "功能管理", "features", "/administration/features", "control", 43, "FeatureManagement.ManageHostFeatures"),
        new("localization", "administration", MenuTypeEnum.Catalog, "本地化管理", "localization", "/administration/localization", "global", 44),
        new("localization-languages", "localization", MenuTypeEnum.Menu, "语言管理", "languages", "/administration/localization/languages", null, 44, "AbpAdmin.Languages"),
        new("localization-texts", "localization", MenuTypeEnum.Menu, "本地化文本", "language-texts", "/administration/localization/texts", null, 45, "AbpAdmin.LanguageTexts"),
        new("text-templates", "administration", MenuTypeEnum.Menu, "文本模板", "text-templates", "/administration/text-templates", "fileText", 45, "AbpAdmin.TextTemplates"),
        new("audit-logs", "administration", MenuTypeEnum.Menu, "审计日志", "audit-logs", "/administration/audit-logs", "fileSearch", 46, "AbpAdmin.AuditLogs"),
        new("operation-logs", "administration", MenuTypeEnum.Menu, "操作日志", "operation-logs", "/administration/operation-logs", "history", 47, "AbpAdmin.OperationLogs"),
        new("files", "administration", MenuTypeEnum.Menu, "文件管理", "files", "/administration/files", "folder", 48, "EasyAbp.FileManagement.File"),
        new("payments", "administration", MenuTypeEnum.Menu, "支付", "payments", "/administration/payments", "payCircle", 49, "AbpAdmin.Payments"),
        new("menus", "administration", MenuTypeEnum.Menu, "菜单管理", "menus", "/administration/menus", "menu", 50, "AbpAdmin.Menus"),
        new("settings", "administration", MenuTypeEnum.Menu, "设置管理", "settings", "/administration/settings", "setting", 51, "SettingUi.ShowSettingPage"),

        // ===== SaaS（ABP Pro 形态：独立顶层组，路由仍驻 /administration/saas/* 保持 URL 兼容） =====
        new("saas", null, MenuTypeEnum.Catalog, "SaaS", "saas", "/administration/saas", "cloud", 60),
        new("saas-tenants", "saas", MenuTypeEnum.Menu, "租户", "tenants", "/administration/saas/tenants", null, 60, "AbpTenantManagement.Tenants"),
        new("saas-editions", "saas", MenuTypeEnum.Menu, "版本", "editions", "/administration/saas/editions", null, 61, "AbpAdmin.Editions"),
        new("saas-tenant-packages", "saas", MenuTypeEnum.Menu, "租户套餐", "tenant-packages", "/administration/saas/tenant-packages", "cluster", 62, "AbpAdmin.TenantPackages"),

        // ===== 系统（运维监控） =====
        new("system", null, MenuTypeEnum.Catalog, "系统", "system", "/system", "desktop", 70),
        new("background-jobs", "system", MenuTypeEnum.Menu, "后台作业", "background-jobs", "/system/background-jobs", "thunderbolt", 70, "AbpAdmin.BackgroundJobs"),
        new("scheduled-jobs", "system", MenuTypeEnum.Menu, "定时作业", "scheduled-jobs", "/system/scheduled-jobs", "clockCircle", 71, "AbpAdmin.ScheduledJobs"),
        new("notifications", "system", MenuTypeEnum.Menu, "通知", "notifications", "/system/notifications", "bell", 72, "EasyAbp.NotificationService.Notification.Manage"),
        new("data-dictionary", "system", MenuTypeEnum.Menu, "数据字典", "data-dictionary", "/system/data-dictionary", "table", 73, "EasyAbp.Abp.DataDictionary.DataDictionary"),
        new("virtual-file-explorer", "system", MenuTypeEnum.Menu, "虚拟文件浏览", "virtual-file-explorer", "/system/virtual-file-explorer", "folderOpen", 74, "AbpAdmin.VirtualFileExplorer"),
        new("server-monitor", "system", MenuTypeEnum.Menu, "服务监控", "server-monitor", "/system/server-monitor", "dashboard", 75, "AbpAdmin.ServerMonitor"),
        new("cache-monitor", "system", MenuTypeEnum.Menu, "缓存监控", "cache-monitor", "/system/cache-monitor", "hdd", 76, "AbpAdmin.CacheMonitor"),

        // ===== 示例演示（置底区隔正式功能；路由保持顶层不变） =====
        new("demos", null, MenuTypeEnum.Catalog, "示例演示", "demos", "/demos", "experiment", 90),
        new("data-scope-demo", "demos", MenuTypeEnum.Menu, "数据权限演示", "data-scope-demo", "/data-scope-demo", "database", 90, "AbpAdmin.DataScopes.Demo"),
        new("biz-template-data-scope", "demos", MenuTypeEnum.Menu, "业务模块·数据权限示例", "biz-template-data-scope", "/biz-template/data-scope", "apartment", 91, "BizTemplate.DataScopeDemo"),

        // ===== 个人项置底 =====
        new("current-session", null, MenuTypeEnum.Menu, "当前会话", "current-session", "/current-session", "user", 100),
    };
}
