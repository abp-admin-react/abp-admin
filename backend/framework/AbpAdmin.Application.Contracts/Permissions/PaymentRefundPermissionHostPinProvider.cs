using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Permissions;

/// <summary>
/// NV-25：EasyAbp PaymentService 包把 Refund 权限（含 Refund.Manage 子位）定义成双侧可授
/// （反编译定案：PaymentServicePermissionDefinitionProvider 里 MultiTenancySides.Both），
/// 租户创建种子会给每个租户 admin 授予它——包 HttpApi 的 RefundController
/// （/api/payment-service/refund）就成了一条绕过 repo 侧 IPaymentRefundGuard
/// （CollectionRefundGuard 的收款保护）的平行走道：包代码引用不到 repo 的守卫接口。
/// 本 provider 在 PostDefine 阶段（包 provider 的 Define 必已执行，框架三阶段顺序保证）
/// 把退款权限钉到 Host：租户侧定义消失 ⇒ 种子不再授予 ⇒ 新租户拿不到该位。
/// repo 自己的退款路径走 PaymentManager.StartRefundAsync（Domain 层，不经过包应用服务的
/// 授权代理），不受影响；宿主 admin 仍持既有授权——存量租户侧授权行的清理是 owner 动作
/// （见审计 run-1 NV-25 owner 清单）。
/// </summary>
public class PaymentRefundPermissionHostPinProvider : PermissionDefinitionProvider
{
    public const string PaymentServiceGroupName = "EasyAbp.PaymentService";

    public static readonly string[] RefundPermissionNames =
    {
        "EasyAbp.PaymentService.Refund",
        "EasyAbp.PaymentService.Refund.Manage",
    };

    public override void Define(IPermissionDefinitionContext context)
    {
        // 本 provider 只做 PostDefine 侧改写；Define 阶段不新增任何定义。
    }

    public override void PostDefine(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull(PaymentServiceGroupName);
        if (group == null)
        {
            return;
        }

        foreach (var name in RefundPermissionNames)
        {
            var permission = group.GetPermissionOrNull(name);
            if (permission != null)
            {
                permission.MultiTenancySide = MultiTenancySides.Host;
            }
        }
    }
}
