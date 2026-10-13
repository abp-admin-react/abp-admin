using AbpAdmin.Biz.Template.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace AbpAdmin.Biz.Template.Permissions;

/// <summary>
/// 模块自带权限定义：不往框架 AbpAdminPermissionDefinitionProvider 里加业务权限，
/// 权限显示名随模块本地化资源走（业务归业务）。
/// </summary>
public class BizTemplatePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(BizTemplatePermissions.GroupName, L("Permission:BizTemplate"));

        var projects = group.AddPermission(BizTemplatePermissions.Projects.Default, L("Permission:Projects"));
        projects.AddChild(BizTemplatePermissions.Projects.Create, L("Permission:Create"));
        projects.AddChild(BizTemplatePermissions.Projects.Update, L("Permission:Update"));
        projects.AddChild(BizTemplatePermissions.Projects.Delete, L("Permission:Delete"));

        // 数据权限示例：消费框架数据范围基建的最小业务面（见 BizDataScopeDemoAppService）
        group.AddPermission(BizTemplatePermissions.DataScopeDemo.Default, L("Permission:DataScopeDemo"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<BizTemplateResource>(name);
    }
}
