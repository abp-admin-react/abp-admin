using System.Collections.Generic;

namespace AbpAdmin.Menus;

/// <summary>
/// 全局菜单模板的节点定义（纯数据，与 Domain 行为分离）。
/// 模板镜像前端 config/routes.ts 的菜单层级（ABP 官方模板顺序：Saas → Identity → … → Settings），
/// 权限绑定与 src/access.ts 的 grantedPolicies 映射一致。Domain 层不引用 Application.Contracts，
/// 权限名用字面量（与各模块 PermissionDefinitionProvider 注册名一致，稳定不变）。
/// <para>Title 是「locale key 缺失时的中文兜底文案」（国际化走前端 menu.{parent}.{name} 语言包），
/// 不是正规文案出处——英文环境首访兜底生效时菜单显示中文属预期行为。</para>
/// </summary>
public static class MenuTemplateDefinition
{
    /// <summary>
    /// 全局模板树。新增页面时在 routes.ts 加路由、在前端语言包加 menu.* key，并在这里补一个节点。
    /// </summary>
    public static List<MenuDefinition> All => new()
    {
        new("administration", null, MenuTypeEnum.Catalog, "管理", "administration", "/administration", "appstore", 20),
        new("saas", "administration", MenuTypeEnum.Catalog, "SaaS", "saas", "/administration/saas", null, 10),
        new("saas-tenants", "saas", MenuTypeEnum.Menu, "租户", "tenants", "/administration/saas/tenants", null, 10, "AbpTenantManagement.Tenants"),
        new("saas-editions", "saas", MenuTypeEnum.Menu, "版本", "editions", "/administration/saas/editions", null, 20, "AbpAdmin.Editions"),
        new("saas-tenant-packages", "saas", MenuTypeEnum.Menu, "租户套餐", "tenant-packages", "/administration/saas/tenant-packages", "cluster", 30, "AbpAdmin.TenantPackages"),
        new("identity", "administration", MenuTypeEnum.Catalog, "身份管理", "identity", "/administration/identity", "team", 20),
        new("identity-users", "identity", MenuTypeEnum.Menu, "用户", "users", "/administration/identity/users", null, 10, "AbpIdentity.Users"),
        new("identity-roles", "identity", MenuTypeEnum.Menu, "角色", "roles", "/administration/identity/roles", null, 20, "AbpIdentity.Roles"),
        new("identity-organization-units", "identity", MenuTypeEnum.Menu, "组织单元", "organization-units", "/administration/identity/organization-units", null, 30, "AbpAdmin.OrganizationUnits"),
        new("identity-posts", "identity", MenuTypeEnum.Menu, "岗位", "posts", "/administration/identity/posts", null, 35, "AbpAdmin.Posts"),
        new("identity-claim-types", "identity", MenuTypeEnum.Menu, "声明类型", "claim-types", "/administration/identity/claim-types", null, 40, "AbpAdmin.ClaimTypes"),
        new("identity-security-logs", "identity", MenuTypeEnum.Menu, "安全日志", "security-logs", "/administration/identity/security-logs", null, 50, "AbpAdmin.SecurityLogs"),
        new("identity-sessions", "identity", MenuTypeEnum.Menu, "会话", "sessions", "/administration/identity/sessions", null, 60, "AbpAdmin.Sessions"),
        new("openiddict", "administration", MenuTypeEnum.Catalog, "OpenIddict", "openiddict", "/administration/openiddict", "safety", 30),
        new("openiddict-applications", "openiddict", MenuTypeEnum.Menu, "应用程序", "applications", "/administration/openiddict/applications", null, 10, "AbpAdmin.OpenIddict.Applications"),
        new("openiddict-scopes", "openiddict", MenuTypeEnum.Menu, "范围", "scopes", "/administration/openiddict/scopes", null, 20, "AbpAdmin.OpenIddict.Scopes"),
        new("openiddict-tokens", "openiddict", MenuTypeEnum.Menu, "令牌管理", "token-management", "/administration/openiddict/token-management", null, 30, "AbpAdmin.OpenIddict.Tokens"),
        new("features", "administration", MenuTypeEnum.Menu, "功能管理", "features", "/administration/features", "control", 40, "FeatureManagement.ManageHostFeatures"),
        new("localization", "administration", MenuTypeEnum.Catalog, "本地化管理", "localization", "/administration/localization", "global", 50),
        new("localization-languages", "localization", MenuTypeEnum.Menu, "语言管理", "languages", "/administration/localization/languages", null, 10, "AbpAdmin.Languages"),
        new("localization-texts", "localization", MenuTypeEnum.Menu, "本地化文本", "language-texts", "/administration/localization/texts", null, 20, "AbpAdmin.LanguageTexts"),
        new("text-templates", "administration", MenuTypeEnum.Menu, "文本模板", "text-templates", "/administration/text-templates", "fileText", 60, "AbpAdmin.TextTemplates"),
        new("audit-logs", "administration", MenuTypeEnum.Menu, "审计日志", "audit-logs", "/administration/audit-logs", "fileSearch", 70, "AbpAdmin.AuditLogs"),
        new("operation-logs", "administration", MenuTypeEnum.Menu, "操作日志", "operation-logs", "/administration/operation-logs", "history", 75, "AbpAdmin.OperationLogs"),
        new("files", "administration", MenuTypeEnum.Menu, "文件管理", "files", "/administration/files", "folder", 80, "EasyAbp.FileManagement.File"),
        new("payments", "administration", MenuTypeEnum.Menu, "支付", "payments", "/administration/payments", "payCircle", 90, "AbpAdmin.Payments"),
        new("menus", "administration", MenuTypeEnum.Menu, "菜单管理", "menus", "/administration/menus", "menu", 95, "AbpAdmin.Menus"),
        new("settings", "administration", MenuTypeEnum.Menu, "设置管理", "settings", "/administration/settings", "setting", 100, "SettingUi.ShowSettingPage"),
        new("system", null, MenuTypeEnum.Catalog, "系统", "system", "/system", "desktop", 30),
        new("background-jobs", "system", MenuTypeEnum.Menu, "后台作业", "background-jobs", "/system/background-jobs", "thunderbolt", 10, "AbpAdmin.BackgroundJobs"),
        new("scheduled-jobs", "system", MenuTypeEnum.Menu, "定时作业", "scheduled-jobs", "/system/scheduled-jobs", "clockCircle", 20, "AbpAdmin.ScheduledJobs"),
        new("notifications", "system", MenuTypeEnum.Menu, "通知", "notifications", "/system/notifications", "bell", 30, "EasyAbp.NotificationService.Notification.Manage"),
        new("data-dictionary", "system", MenuTypeEnum.Menu, "数据字典", "data-dictionary", "/system/data-dictionary", "table", 40, "EasyAbp.Abp.DataDictionary.DataDictionary"),
        new("virtual-file-explorer", "system", MenuTypeEnum.Menu, "虚拟文件浏览", "virtual-file-explorer", "/system/virtual-file-explorer", "folderOpen", 50, "AbpAdmin.VirtualFileExplorer"),
        new("server-monitor", "system", MenuTypeEnum.Menu, "服务监控", "server-monitor", "/system/server-monitor", "dashboard", 55, "AbpAdmin.ServerMonitor"),
        new("cache-monitor", "system", MenuTypeEnum.Menu, "缓存监控", "cache-monitor", "/system/cache-monitor", "hdd", 60, "AbpAdmin.CacheMonitor"),
        new("data-scope-demo", null, MenuTypeEnum.Menu, "数据权限演示", "data-scope-demo", "/data-scope-demo", "database", 40, "AbpAdmin.DataScopes.Demo"),
        new("current-session", null, MenuTypeEnum.Menu, "当前会话", "current-session", "/current-session", "user", 50),
    };
}
