using AbpAdmin.Webhooks.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace AbpAdmin.Webhooks.Permissions;

/// <summary>Webhook 权限常量。管理面收敛到一个独立权限组（不是框架 AbpAdmin 组——模块自持）。</summary>
public static class WebhooksPermissions
{
    public const string GroupName = "AbpAdmin.Webhooks";

    public static class Subscriptions
    {
        public const string Default = GroupName + ".Subscriptions";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class SendRecords
    {
        public const string Default = GroupName + ".SendRecords";
        public const string Resend = Default + ".Resend";
        public const string Delete = Default + ".Delete";
    }
}

/// <summary>权限定义提供者（模块自持，随模块装配生效）。</summary>
public class WebhooksPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(WebhooksPermissions.GroupName, L("Permission:Webhooks"));

        var subscriptions = group.AddPermission(WebhooksPermissions.Subscriptions.Default, L("Permission:Subscriptions"));
        subscriptions.AddChild(WebhooksPermissions.Subscriptions.Create, L("Permission:Subscriptions.Create"));
        subscriptions.AddChild(WebhooksPermissions.Subscriptions.Update, L("Permission:Subscriptions.Update"));
        subscriptions.AddChild(WebhooksPermissions.Subscriptions.Delete, L("Permission:Subscriptions.Delete"));

        var records = group.AddPermission(WebhooksPermissions.SendRecords.Default, L("Permission:SendRecords"));
        records.AddChild(WebhooksPermissions.SendRecords.Resend, L("Permission:SendRecords.Resend"));
        records.AddChild(WebhooksPermissions.SendRecords.Delete, L("Permission:SendRecords.Delete"));
    }

    private static LocalizableString L(string name) =>
        LocalizableString.Create<WebhooksResource>(name);
}
