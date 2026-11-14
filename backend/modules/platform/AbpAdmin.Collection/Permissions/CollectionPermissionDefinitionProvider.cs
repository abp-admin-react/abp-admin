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
        group.AddPermission(CollectionPermissions.Payments.View, L("Permission:Payments.View"),
            multiTenancySide: MultiTenancySides.Host);
        group.AddPermission(CollectionPermissions.Payments.Reconcile, L("Permission:Payments.Reconcile"),
            multiTenancySide: MultiTenancySides.Host);
        group.AddPermission(CollectionPermissions.Payments.Refund, L("Permission:Payments.Refund"),
            multiTenancySide: MultiTenancySides.Host);
        group.AddPermission(CollectionPermissions.BalanceAdjustments.View, L("Permission:BalanceAdjustments.View"),
            multiTenancySide: MultiTenancySides.Host);
        group.AddPermission(CollectionPermissions.BalanceAdjustments.Create, L("Permission:BalanceAdjustments.Create"),
            multiTenancySide: MultiTenancySides.Host);
        group.AddPermission(CollectionPermissions.BalanceAdjustments.Approve, L("Permission:BalanceAdjustments.Approve"),
            multiTenancySide: MultiTenancySides.Host);
        group.AddPermission(CollectionPermissions.BalanceAdjustments.Reject, L("Permission:BalanceAdjustments.Reject"),
            multiTenancySide: MultiTenancySides.Host);
        // ABP grants every role-capable permission to admin during seeding. Self-review must be an explicit user exception.
        group.AddPermission(CollectionPermissions.BalanceAdjustments.ApproveOwn, L("Permission:BalanceAdjustments.ApproveOwn"),
            multiTenancySide: MultiTenancySides.Host).WithProviders(UserPermissionValueProvider.ProviderName);
        // 链网运维仅 Host（multiTenancySide 限定 + 管理服务 AssertHost 双保险）：
        // 否则租户管理员可自授 Network.Admin 操作 Host 的共享网络行（助记词/金库/RPC）
        group.AddPermission(CollectionPermissions.Network.Admin, L("Permission:Network.Admin"),
            multiTenancySide: MultiTenancySides.Host);
        // 提现出账仅 Host（余额/提现表无租户列；租户管理员自授会越过 Host 资金边界）
        group.AddPermission(CollectionPermissions.Withdraw.Admin, L("Permission:WithdrawAdmin"),
            multiTenancySide: MultiTenancySides.Host);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CollectionResource>(name);
    }
}
