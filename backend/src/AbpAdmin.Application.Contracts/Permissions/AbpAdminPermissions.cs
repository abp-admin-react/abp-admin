using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Permissions;

public static class AbpAdminPermissions
{
    public const string GroupName = "AbpAdmin";

    public static class OrganizationUnits
    {
        public const string Default = GroupName + ".OrganizationUnits";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ManageMembers = Default + ".ManageMembers";
        public const string ManageRoles = Default + ".ManageRoles";
    }

    public static class ClaimTypes
    {
        public const string Default = GroupName + ".ClaimTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class SecurityLogs
    {
        public const string Default = GroupName + ".SecurityLogs";
    }

    /// <summary>岗位管理（对标 RuoYi sys_post）：职务维度，与组织单元（部门维度）互补。</summary>
    public static class Posts
    {
        public const string Default = GroupName + ".Posts";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ManageMembers = Default + ".ManageMembers";
    }

    public static class Sessions
    {
        public const string Default = GroupName + ".Sessions";
        public const string Revoke = Default + ".Revoke";
    }

    public static class AuditLogs
    {
        public const string Default = GroupName + ".AuditLogs";
        public const string Export = Default + ".Export";
        public const string HandleErrors = Default + ".HandleErrors";
    }

    /// <summary>语义化操作日志（业务人员可读的「谁对谁做了什么」），与审计日志互补。</summary>
    public static class OperationLogs
    {
        public const string Default = GroupName + ".OperationLogs";
    }

    public static class AuditLogging
    {
        public const string GroupPrefix = GroupName + ".AuditLogging";
        public const string ViewChangeHistoryPrefix = GroupPrefix + ".ViewChangeHistory:";
        public static string ViewChangeHistory(string entityTypeFullName)
            => ViewChangeHistoryPrefix + entityTypeFullName;
    }

    public static class OpenIddict
    {
        public const string Default = GroupName + ".OpenIddict";

        public static class Applications
        {
            public const string Default = OpenIddict.Default + ".Applications";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
            public const string ManagePermissions = Default + ".ManagePermissions";
            public const string GenerateAccessToken = Default + ".GenerateAccessToken";
        }

        public static class Scopes
        {
            public const string Default = OpenIddict.Default + ".Scopes";
            public const string Create = Default + ".Create";
            public const string Update = Default + ".Update";
            public const string Delete = Default + ".Delete";
        }

        /// <summary>令牌/授权管理：查看、吊销、清理过期（对应芋道"令牌管理"）。</summary>
        public static class Tokens
        {
            public const string Default = OpenIddict.Default + ".Tokens";
            public const string Revoke = Default + ".Revoke";
            public const string Prune = Default + ".Prune";
        }
    }

    public static class Editions
    {
        public const string Default = GroupName + ".Editions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string ManageFeatures = Default + ".ManageFeatures";
    }

    public static class TextTemplates
    {
        public const string Default = GroupName + ".TextTemplates";
        public const string Update = Default + ".Update";
        /// <summary>
        /// 编辑非沙箱模板内容的权限。
        /// 警告：此权限等同于授予持有者应用服务器的 shell 访问权（非沙箱模板引擎可以在模板里执行任意代码）。
        /// </summary>
        public const string EditNonSandboxedContents = Default + ".EditNonSandboxedContents";
    }

    public static class Languages
    {
        public const string Default = GroupName + ".Languages";
        public const string Create = Default + ".Create";          // host only
        public const string Update = Default + ".Update";          // host only
        public const string Delete = Default + ".Delete";          // host only
        public const string ChangeDefault = Default + ".ChangeDefault";  // host + tenant
    }

    public static class LanguageTexts
    {
        public const string Default = GroupName + ".LanguageTexts";   // host + tenant
        public const string Edit = Default + ".Edit";                 // host + tenant
    }

    public static class BackgroundJobs
    {
        public const string Default = GroupName + ".BackgroundJobs";
        public const string Delete = Default + ".Delete";
        public const string Abandon = Default + ".Abandon";
        public const string Retry = Default + ".Retry";
        public const string Enqueue = Default + ".Enqueue";
    }

    public static class ScheduledJobs
    {
        public const string Default = GroupName + ".ScheduledJobs";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Trigger = Default + ".Trigger";
    }

    public static class VirtualFileExplorer
    {
        public const string Default = GroupName + ".VirtualFileExplorer";
    }

    /// <summary>服务监控（Host 专属）：进程/机器/CPU/内存/GC/磁盘只读指标。</summary>
    public static class ServerMonitor
    {
        public const string Default = GroupName + ".ServerMonitor";
    }

    /// <summary>缓存监控（Host 专属）：分布式缓存键浏览、取值、删除。</summary>
    public static class CacheMonitor
    {
        public const string Default = GroupName + ".CacheMonitor";
        public const string Manage = Default + ".Manage";
    }

    public static class DataScopes
    {
        public const string Default = GroupName + ".DataScopes";
        public const string Manage = Default + ".Manage";
        public const string Demo = Default + ".Demo";
    }

    public static class Identity
    {
        public const string Default = GroupName + ".Identity";

        public static class Users
        {
            public const string Default = Identity.Default + ".Users";
            public const string Import = Default + ".Import";
            public const string Export = Default + ".Export";
        }
    }

    public static class Impersonation
    {
        public const string Default = GroupName + ".Impersonation";
        public const string Tenant = Default + ".Tenant";
        public const string User = Default + ".User";
    }

    public static class Payments
    {
        public const string Default = GroupName + ".Payments";
        public const string Manage = Default + ".Manage";
        public const string Refund = Default + ".Refund";
        public const string Prepayment = Default + ".Prepayment";
    }

    /// <summary>菜单管理（动态菜单）。租户管理员也可维护本租户菜单树，非 Host-only。</summary>
    public static class Menus
    {
        public const string Default = GroupName + ".Menus";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string AssignRoles = Default + ".AssignRoles";
    }

    /// <summary>租户套餐（Host 专属）：打包全局菜单模板子集，创建租户时按套餐下发菜单。</summary>
    public static class TenantPackages
    {
        public const string Default = GroupName + ".TenantPackages";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>
    /// SettingUi 写权限（读 = 上游 SettingUi.ShowSettingPage，写 = 本权限）。
    /// 对齐官方 setting-management 的粒度思路（读写分离）：改 SMTP/短信路由属敏感操作，
    /// 与"能看到设置页"分开授权。注意持有本权限还需同时持有 ShowSettingPage
    /// （AbpAdminSettingUiAppService 类级 Authorize 对所有方法生效）。
    /// </summary>
    public static class SettingUi
    {
        public const string Update = GroupName + ".SettingUi.Update";
    }

    // 问题15 修复：已删除无调用方的 GetAll()——它会把 GroupName（"AbpAdmin"）与
    // 动态前缀 ViewChangeHistoryPrefix 也当权限名返回，误用即事故。权限定义树的
    // 一致性校验由权限种子/DefinitionProvider 的测试承担。
}
