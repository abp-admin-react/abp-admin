using AbpAdmin.AuditLogs;
using AbpAdmin.Localization;
using EasyAbp.Abp.SettingUi.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Permissions;

public class AbpAdminPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(AbpAdminPermissions.GroupName, L("Permission:AbpAdmin"));

        var ous = group.AddPermission(
            AbpAdminPermissions.OrganizationUnits.Default,
            L("Permission:OrganizationUnits"));
        ous.AddChild(AbpAdminPermissions.OrganizationUnits.Create, L("Permission:Create"));
        ous.AddChild(AbpAdminPermissions.OrganizationUnits.Update, L("Permission:Update"));
        ous.AddChild(AbpAdminPermissions.OrganizationUnits.Delete, L("Permission:Delete"));
        ous.AddChild(AbpAdminPermissions.OrganizationUnits.ManageMembers, L("Permission:ManageMembers"));
        ous.AddChild(AbpAdminPermissions.OrganizationUnits.ManageRoles, L("Permission:ManageRoles"));

        var claims = group.AddPermission(
            AbpAdminPermissions.ClaimTypes.Default,
            L("Permission:ClaimTypes"));
        claims.AddChild(AbpAdminPermissions.ClaimTypes.Create, L("Permission:Create"));
        claims.AddChild(AbpAdminPermissions.ClaimTypes.Update, L("Permission:Update"));
        claims.AddChild(AbpAdminPermissions.ClaimTypes.Delete, L("Permission:Delete"));

        group.AddPermission(
            AbpAdminPermissions.SecurityLogs.Default,
            L("Permission:SecurityLogs"));

        var posts = group.AddPermission(
            AbpAdminPermissions.Posts.Default,
            L("Permission:Posts"));
        posts.AddChild(AbpAdminPermissions.Posts.Create, L("Permission:Create"));
        posts.AddChild(AbpAdminPermissions.Posts.Update, L("Permission:Update"));
        posts.AddChild(AbpAdminPermissions.Posts.Delete, L("Permission:Delete"));
        posts.AddChild(AbpAdminPermissions.Posts.ManageMembers, L("Permission:ManageMembers"));

        var sessions = group.AddPermission(
            AbpAdminPermissions.Sessions.Default,
            L("Permission:Sessions"));
        sessions.AddChild(AbpAdminPermissions.Sessions.Revoke, L("Permission:Revoke"));

        var auditLogs = group.AddPermission(
            AbpAdminPermissions.AuditLogs.Default,
            L("Permission:AuditLogs"));
        auditLogs.AddChild(AbpAdminPermissions.AuditLogs.Export, L("Permission:Export"));
        auditLogs.AddChild(AbpAdminPermissions.AuditLogs.HandleErrors, L("Permission:AuditLogs.HandleErrors"));

        group.AddPermission(
            AbpAdminPermissions.OperationLogs.Default,
            L("Permission:OperationLogs"));

        // 实体级变更历史权限，按 Pro 的命名约定动态定义
        foreach (var entityType in AuditLoggingEntityTypes.ChangeHistoryEnabled)
        {
            auditLogs.AddChild(
                AbpAdminPermissions.AuditLogging.ViewChangeHistory(entityType),
                L("Permission:ViewChangeHistory"));
        }

        var openIddict = group.AddPermission(
            AbpAdminPermissions.OpenIddict.Default,
            L("Permission:OpenIddict"),
            multiTenancySide: MultiTenancySides.Host);

        var apps = openIddict.AddChild(
            AbpAdminPermissions.OpenIddict.Applications.Default,
            L("Permission:OpenIddict.Applications"),
            multiTenancySide: MultiTenancySides.Host);
        apps.AddChild(AbpAdminPermissions.OpenIddict.Applications.Create, L("Permission:Create"), MultiTenancySides.Host);
        apps.AddChild(AbpAdminPermissions.OpenIddict.Applications.Update, L("Permission:Update"), MultiTenancySides.Host);
        apps.AddChild(AbpAdminPermissions.OpenIddict.Applications.Delete, L("Permission:Delete"), MultiTenancySides.Host);
        apps.AddChild(AbpAdminPermissions.OpenIddict.Applications.ManagePermissions, L("Permission:ManagePermissions"), MultiTenancySides.Host);
        apps.AddChild(AbpAdminPermissions.OpenIddict.Applications.GenerateAccessToken, L("Permission:OpenIddict.Applications.GenerateAccessToken"), MultiTenancySides.Host);

        var scopes = openIddict.AddChild(
            AbpAdminPermissions.OpenIddict.Scopes.Default,
            L("Permission:OpenIddict.Scopes"),
            multiTenancySide: MultiTenancySides.Host);
        scopes.AddChild(AbpAdminPermissions.OpenIddict.Scopes.Create, L("Permission:Create"), MultiTenancySides.Host);
        scopes.AddChild(AbpAdminPermissions.OpenIddict.Scopes.Update, L("Permission:Update"), MultiTenancySides.Host);
        scopes.AddChild(AbpAdminPermissions.OpenIddict.Scopes.Delete, L("Permission:Delete"), MultiTenancySides.Host);

        var tokens = openIddict.AddChild(
            AbpAdminPermissions.OpenIddict.Tokens.Default,
            L("Permission:OpenIddict.Tokens"),
            multiTenancySide: MultiTenancySides.Host);
        tokens.AddChild(AbpAdminPermissions.OpenIddict.Tokens.Revoke, L("Permission:OpenIddict.Tokens.Revoke"), MultiTenancySides.Host);
        tokens.AddChild(AbpAdminPermissions.OpenIddict.Tokens.Prune, L("Permission:OpenIddict.Tokens.Prune"), MultiTenancySides.Host);

        var editions = group.AddPermission(
            AbpAdminPermissions.Editions.Default,
            L("Permission:Editions"),
            multiTenancySide: MultiTenancySides.Host);
        editions.AddChild(AbpAdminPermissions.Editions.Create, L("Permission:Create"), MultiTenancySides.Host);
        editions.AddChild(AbpAdminPermissions.Editions.Update, L("Permission:Update"), MultiTenancySides.Host);
        editions.AddChild(AbpAdminPermissions.Editions.Delete, L("Permission:Delete"), MultiTenancySides.Host);
        editions.AddChild(AbpAdminPermissions.Editions.ManageFeatures, L("Permission:ManageFeatures"), MultiTenancySides.Host);

        var templates = group.AddPermission(
            AbpAdminPermissions.TextTemplates.Default,
            L("Permission:TextTemplates"));
        templates.AddChild(AbpAdminPermissions.TextTemplates.Update, L("Permission:Update"));
        templates.AddChild(AbpAdminPermissions.TextTemplates.EditNonSandboxedContents, L("Permission:EditNonSandboxedContents"));

        var languages = group.AddPermission(
            AbpAdminPermissions.Languages.Default,
            L("Permission:Languages"));
        languages.AddChild(AbpAdminPermissions.Languages.Create, L("Permission:Create"), MultiTenancySides.Host);
        languages.AddChild(AbpAdminPermissions.Languages.Update, L("Permission:Update"), MultiTenancySides.Host);
        languages.AddChild(AbpAdminPermissions.Languages.Delete, L("Permission:Delete"), MultiTenancySides.Host);
        languages.AddChild(AbpAdminPermissions.Languages.ChangeDefault, L("Permission:ChangeDefault"));

        var languageTexts = group.AddPermission(
            AbpAdminPermissions.LanguageTexts.Default,
            L("Permission:LanguageTexts"));
        languageTexts.AddChild(AbpAdminPermissions.LanguageTexts.Edit, L("Permission:Edit"));

        // 后台作业是宿主级基础设施（AbpBackgroundJobs 表无 IMultiTenant、队列含所有租户的作业与
        // JobArgs 明文）：对租户开放等于让租户管理员看到其他租户的作业参数、重放/放弃他们的导出与
        // 邮件作业。与 ServerMonitor/CacheMonitor 同口径收敛为 Host 专属。
        var jobs = group.AddPermission(
            AbpAdminPermissions.BackgroundJobs.Default,
            L("Permission:BackgroundJobs"),
            multiTenancySide: MultiTenancySides.Host);
        jobs.AddChild(AbpAdminPermissions.BackgroundJobs.Delete, L("Permission:Delete"), MultiTenancySides.Host);
        jobs.AddChild(AbpAdminPermissions.BackgroundJobs.Abandon, L("Permission:Abandon"), MultiTenancySides.Host);
        jobs.AddChild(AbpAdminPermissions.BackgroundJobs.Retry, L("Permission:Retry"), MultiTenancySides.Host);
        jobs.AddChild(AbpAdminPermissions.BackgroundJobs.Enqueue, L("Permission:Enqueue"), MultiTenancySides.Host);

        var scheduledJobs = group.AddPermission(
            AbpAdminPermissions.ScheduledJobs.Default,
            L("Permission:ScheduledJobs"));
        scheduledJobs.AddChild(AbpAdminPermissions.ScheduledJobs.Create, L("Permission:Create"));
        scheduledJobs.AddChild(AbpAdminPermissions.ScheduledJobs.Update, L("Permission:Update"));
        scheduledJobs.AddChild(AbpAdminPermissions.ScheduledJobs.Delete, L("Permission:Delete"));
        scheduledJobs.AddChild(AbpAdminPermissions.ScheduledJobs.Trigger, L("Permission:Trigger"));

        group.AddPermission(
            AbpAdminPermissions.VirtualFileExplorer.Default,
            L("Permission:VirtualFileExplorer"));

        group.AddPermission(
            AbpAdminPermissions.ServerMonitor.Default,
            L("Permission:ServerMonitor"),
            multiTenancySide: MultiTenancySides.Host);

        var cacheMonitor = group.AddPermission(
            AbpAdminPermissions.CacheMonitor.Default,
            L("Permission:CacheMonitor"),
            multiTenancySide: MultiTenancySides.Host);
        cacheMonitor.AddChild(AbpAdminPermissions.CacheMonitor.Manage, L("Permission:CacheMonitor.Manage"), MultiTenancySides.Host);

        var dataScopes = group.AddPermission(
            AbpAdminPermissions.DataScopes.Default,
            L("Permission:DataScopes"));
        dataScopes.AddChild(AbpAdminPermissions.DataScopes.Manage, L("Permission:DataScopes.Manage"));
        dataScopes.AddChild(AbpAdminPermissions.DataScopes.Demo, L("Permission:DataScopes.Demo"));

        // T2.6 Identity Pro 缺口：用户导入导出权限
        var identityUsers = group.AddPermission(
            AbpAdminPermissions.Identity.Users.Default,
            L("Permission:Identity.Users"));
        identityUsers.AddChild(AbpAdminPermissions.Identity.Users.Import, L("Permission:Identity.Users.Import"));
        identityUsers.AddChild(AbpAdminPermissions.Identity.Users.Export, L("Permission:Identity.Users.Export"));

        // T2.7 Account Pro 缺口：模拟登录权限
        var impersonation = group.AddPermission(
            AbpAdminPermissions.Impersonation.Default,
            L("Permission:Impersonation"));
        impersonation.AddChild(AbpAdminPermissions.Impersonation.Tenant, L("Permission:Impersonation.Tenant"), MultiTenancySides.Host);
        impersonation.AddChild(AbpAdminPermissions.Impersonation.User, L("Permission:Impersonation.User"));

        var payments = group.AddPermission(
            AbpAdminPermissions.Payments.Default,
            L("Permission:Payments"));
        payments.AddChild(AbpAdminPermissions.Payments.Manage, L("Permission:Payments.Manage"));
        payments.AddChild(AbpAdminPermissions.Payments.Refund, L("Permission:Payments.Refund"));
        payments.AddChild(AbpAdminPermissions.Payments.Prepayment, L("Permission:Payments.Prepayment"));

        var menus = group.AddPermission(
            AbpAdminPermissions.Menus.Default,
            L("Permission:Menus"));
        menus.AddChild(AbpAdminPermissions.Menus.Create, L("Permission:Menus.Create"));
        menus.AddChild(AbpAdminPermissions.Menus.Update, L("Permission:Menus.Update"));
        menus.AddChild(AbpAdminPermissions.Menus.Delete, L("Permission:Menus.Delete"));
        menus.AddChild(AbpAdminPermissions.Menus.AssignRoles, L("Permission:Menus.AssignRoles"));

        var tenantPackages = group.AddPermission(
            AbpAdminPermissions.TenantPackages.Default,
            L("Permission:TenantPackages"),
            multiTenancySide: MultiTenancySides.Host);
        tenantPackages.AddChild(AbpAdminPermissions.TenantPackages.Create, L("Permission:TenantPackages.Create"), MultiTenancySides.Host);
        tenantPackages.AddChild(AbpAdminPermissions.TenantPackages.Update, L("Permission:TenantPackages.Update"), MultiTenancySides.Host);
        tenantPackages.AddChild(AbpAdminPermissions.TenantPackages.Delete, L("Permission:TenantPackages.Delete"), MultiTenancySides.Host);

        // SettingUi 读写分离（模块五收口的对齐项）：写权限挂在上游 SettingUi 权限组下，
        // 与 ShowSettingPage（读）并列展示。模块依赖已声明 AbpSettingUiApplicationContractsModule，
        // 上游 provider 先执行，GetGroupOrNull 必中。
        var settingUiGroup = context.GetGroupOrNull(SettingUiPermissions.GroupName)
            ?? context.AddGroup(SettingUiPermissions.GroupName, L("Permission:SettingUi"));
        settingUiGroup.AddPermission(
            AbpAdminPermissions.SettingUi.Update,
            L("Permission:SettingUi.Update"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpAdminResource>(name);
    }
}
