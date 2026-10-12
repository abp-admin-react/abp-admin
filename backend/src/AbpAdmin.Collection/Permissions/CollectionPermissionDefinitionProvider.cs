using AbpAdmin.Collection.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Collection.Permissions;

/// <summary>
/// 收款模块权限定义：显示名随模块本地化资源走（同 Biz.Template 纪律）。
/// </summary>
public class CollectionPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CollectionPermissions.GroupName, L("Permission:Collection"));
        group.AddPermission(CollectionPermissions.Collections.Default, L("Permission:Collections"));
        // 链网运维仅 Host（multiTenancySide 限定 + 管理服务 AssertHost 双保险，评审 H 级）：
        // 否则租户管理员可自授 Network.Admin 操作 Host 的共享网络行（助记词/金库/RPC）
        group.AddPermission(CollectionPermissions.Network.Admin, L("Permission:Network.Admin"),
            multiTenancySide: MultiTenancySides.Host);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CollectionResource>(name);
    }
}
