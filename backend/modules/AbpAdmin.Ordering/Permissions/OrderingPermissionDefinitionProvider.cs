using AbpAdmin.Ordering.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace AbpAdmin.Ordering.Permissions;

/// <summary>
/// 订单模块权限定义：不往框架 AbpAdminPermissionDefinitionProvider 里加业务权限，
/// 显示名随模块本地化资源走（同 Biz.Template 纪律）。
/// </summary>
public class OrderingPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(OrderingPermissions.GroupName, L("Permission:Ordering"));

        var orders = group.AddPermission(OrderingPermissions.Orders.Default, L("Permission:Orders"));
        orders.AddChild(OrderingPermissions.Orders.ViewAll, L("Permission:Orders.ViewAll"));
        orders.AddChild(OrderingPermissions.Orders.Fulfill, L("Permission:Orders.Fulfill"));
        orders.AddChild(OrderingPermissions.Orders.RecordManualRefund, L("Permission:Orders.RecordManualRefund"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<OrderingResource>(name);
    }
}
