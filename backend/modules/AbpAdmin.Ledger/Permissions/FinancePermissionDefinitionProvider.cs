using AbpAdmin.Ledger.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace AbpAdmin.Ledger.Permissions;

public class FinancePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(FinancePermissions.GroupName, L("Permission:Finance"));

        var ledgerView = group.AddPermission(FinancePermissions.Ledger.View, L("Permission:Finance.Ledger.View"));
        ledgerView.AddChild(FinancePermissions.Ledger.Export, L("Permission:Finance.Ledger.Export"));

        group.AddPermission(FinancePermissions.Account.Manage, L("Permission:Finance.Account.Manage"));
        group.AddPermission(FinancePermissions.Rule.View, L("Permission:Finance.Rule.View"));

        var voucherView = group.AddPermission(FinancePermissions.Voucher.View, L("Permission:Finance.Voucher.View"));
        voucherView.AddChild(FinancePermissions.Voucher.Reverse, L("Permission:Finance.Voucher.Reverse"));

        // 职责分离全部走权限配置，代码不按角色写死。
        // 默认角色授予（与定稿 §5.2 / LedgerTestModule.TestUsers 一致，生产在角色权限页配置）：
        //   fin_accountant = Create+Submit+Approve+Reject+Reverse（无 ApproveOwn）
        //   fin_boss       = Approve+Reject（无 Create/Submit/Reverse/ApproveOwn）
        //   ApproveOwn 默认不授任何人；自批缺权 → SelfApproveNotAllowed
        group.AddPermission(FinancePermissions.Adjust.Create, L("Permission:Finance.Adjust.Create"));
        group.AddPermission(FinancePermissions.Adjust.Submit, L("Permission:Finance.Adjust.Submit"));
        group.AddPermission(FinancePermissions.Adjust.Approve, L("Permission:Finance.Adjust.Approve"));
        group.AddPermission(FinancePermissions.Adjust.Reject, L("Permission:Finance.Adjust.Reject"));
        group.AddPermission(FinancePermissions.Adjust.Reverse, L("Permission:Finance.Adjust.Reverse"));
        group.AddPermission(FinancePermissions.Adjust.ApproveOwn, L("Permission:Finance.Adjust.ApproveOwn"));

        group.AddPermission(FinancePermissions.Reconcile.Run, L("Permission:Finance.Reconcile.Run"));
        group.AddPermission(FinancePermissions.Period.View, L("Permission:Finance.Period.View"));
        group.AddPermission(FinancePermissions.BizDoc.View, L("Permission:Finance.BizDoc.View"));
        group.AddPermission(FinancePermissions.Balance.View, L("Permission:Finance.Balance.View"));
        group.AddPermission(FinancePermissions.Scope.Agent, L("Permission:Finance.Scope.Agent"));
    }

    private static LocalizableString L(string name) =>
        LocalizableString.Create<LedgerResource>(name);
}
