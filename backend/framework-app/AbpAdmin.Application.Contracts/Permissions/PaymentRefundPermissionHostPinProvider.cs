using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Permissions;

/// <summary>
/// EasyAbp PaymentService 包的钱面权限钉 Host（sectest b03 / 审计 NV-25）。
/// 包把 Refund 与整个 Prepayment 组（Account.Manage.ChangeBalance/ChangeLockedBalance、
/// Account.TopUp、Account.Withdraw、WithdrawalRequest.Review、Transaction.Manage、
/// Payment.Manage.RollbackRefund）都定义成双侧可授（反编译 2.21.0 定案），租户创建种子
/// 给每个租户 admin 授予——包 HttpApi 的 PrepaymentController
/// （/api/payment-service/prepayment/account/{id}/change/balance 等）是绕过 repo 侧
/// 一切资金守卫的平行走道（ChangeBalance 任意符号增量、无付款/复核/审计闸）。
/// 本 provider 在 PostDefine 阶段（包 provider 的 Define 必已执行，框架三阶段顺序保证）
/// 把这些权限钉到 Host：租户侧定义消失 ⇒ 种子不再授予 ⇒ 新租户拿不到。
/// repo 自己的收款/退款路径走 Domain 层守卫（CollectionRefundGuard / PaymentManager），
/// 不经过包应用服务的授权代理，不受影响；宿主 admin 仍持既有授权——存量租户侧授权行
/// 的清理是 owner 动作（AbpPermissionGrants 里 EasyAbp.PaymentService.* 租户行）。
/// 注意：Prepayment 权限住在独立分组 "EasyAbp.PaymentService.Prepayment"（13 个权限），
/// 必须按组分别解析——只遍历主组会拿空（v3-25 沙箱实证）。
///
/// 读提权（sectest p24/p25）：包把 Account/WithdrawalRequest/WithdrawalRecord/Transaction 的
/// *.Manage 也作为「看他人账户」门槛（反编译 GetList/Get 在 UserId≠当前用户时 Check Manage）。
/// 未钉 Host 时租户 admin 凭种子 Manage 可枚举租户内全部钱包/提现申请/流水管理面；
/// 与钱面写权限同口径钉 Host，自助 Default（看自己的账户/流水）仍留 Bilateral。
/// </summary>
public class PaymentRefundPermissionHostPinProvider : PermissionDefinitionProvider
{
    public const string PaymentServiceGroupName = "EasyAbp.PaymentService";
    public const string PrepaymentGroupName = "EasyAbp.PaymentService.Prepayment";

    public static readonly string[] RefundPermissionNames =
    {
        "EasyAbp.PaymentService.Refund",
        "EasyAbp.PaymentService.Refund.Manage",
    };

    // 主组里除 Refund 外的钱面位：回滚在途退款会恢复可退余额（渠道可能仍打款）
    public static readonly string[] PaymentServiceMoneyPermissionNames =
    {
        "EasyAbp.PaymentService.Payment.Manage.RollbackRefund",
    };

    // Prepayment 组的钱面位（名字对齐包 2.21.0 反编译字面量；名字漂移时
    // GetPermissionOrNull 返回 null 跳过，fail-safe 不炸启动，PaymentRefundPermissionPinTests 会红）
    public static readonly string[] PrepaymentMoneyPermissionNames =
    {
        "EasyAbp.PaymentService.Prepayment.Account.Manage",
        "EasyAbp.PaymentService.Prepayment.Account.Manage.ChangeBalance",
        "EasyAbp.PaymentService.Prepayment.Account.Manage.ChangeLockedBalance",
        "EasyAbp.PaymentService.Prepayment.Account.TopUp",
        "EasyAbp.PaymentService.Prepayment.Account.Withdraw",
        "EasyAbp.PaymentService.Prepayment.WithdrawalRequest.Manage",
        "EasyAbp.PaymentService.Prepayment.WithdrawalRequest.Review",
        "EasyAbp.PaymentService.Prepayment.WithdrawalRecord.Manage",
        "EasyAbp.PaymentService.Prepayment.Transaction.Manage",
    };

    public override void Define(IPermissionDefinitionContext context)
    {
        // 本 provider 只做 PostDefine 侧改写；Define 阶段不新增任何定义。
    }

    public override void PostDefine(IPermissionDefinitionContext context)
    {
        PinToHost(context, PaymentServiceGroupName, RefundPermissionNames);
        PinToHost(context, PaymentServiceGroupName, PaymentServiceMoneyPermissionNames);
        PinToHost(context, PrepaymentGroupName, PrepaymentMoneyPermissionNames);
    }

    private static void PinToHost(IPermissionDefinitionContext context, string groupName, string[] permissionNames)
    {
        var group = context.GetGroupOrNull(groupName);
        if (group == null)
        {
            return;
        }

        foreach (var name in permissionNames)
        {
            var permission = group.GetPermissionOrNull(name);
            if (permission != null)
            {
                permission.MultiTenancySide = MultiTenancySides.Host;
            }
        }
    }
}
