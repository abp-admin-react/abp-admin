using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Permissions;

using AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 回归锁：包侧钱面权限必须被钉到 Host（sectest b03 / 审计 NV-25）。
/// 反编译定案：EasyAbp PaymentService 2.21.0 的权限 provider 以 MultiTenancySides.Both
/// 定义 Refund 树与整个 Prepayment 钱面组（ChangeBalance/TopUp/Withdraw/Review/...），
/// 租户创建种子随后授予每个租户 admin——包 HttpApi 的 Refund/Prepayment 控制器因此成为
/// 绕过 repo 侧资金守卫的平行走道。本锁断言 PostDefine 改写生效：租户侧定义消失
/// ⇒ 种子不会再授予新租户。注意 Prepayment 权限住在独立分组，PostDefine 必须按组
/// 分别解析（v3-25 沙箱实证只遍历主组拿空）。
/// </summary>
public class PaymentRefundPermissionPinTests : AbpAdminEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Package_Refund_Permissions_Are_Pinned_To_Host_Side()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in PaymentRefundPermissionHostPinProvider.RefundPermissionNames)
        {
            var definition = await manager.GetAsync(name);
            definition.MultiTenancySide.ShouldBe(MultiTenancySides.Host,
                $"{name} 必须为 Host 侧（租户侧拿不到 = 种子不再授予 = 平行退款门对租户关闭）");
        }
    }

    [Fact]
    public async Task Package_Prepayment_Money_Permissions_Are_Pinned_To_Host_Side()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in PaymentRefundPermissionHostPinProvider.PrepaymentMoneyPermissionNames)
        {
            var definition = await manager.GetAsync(name);
            definition.MultiTenancySide.ShouldBe(MultiTenancySides.Host,
                $"{name} 必须为 Host 侧（租户 admin 凭种子授权即可经包端点 ChangeBalance/Withdraw 铸造钱包余额，"
                + "或凭 Manage 枚举租户内全部钱包/提现申请）");
        }

        // RollbackRefund 同属钱面（恢复在途退款的可退余额，渠道可能仍打款）
        var rollback = await manager.GetAsync("EasyAbp.PaymentService.Payment.Manage.RollbackRefund");
        rollback.MultiTenancySide.ShouldBe(MultiTenancySides.Host);

        // 读提权 Manage 必须在钉列表里（与写面同口径；漂移时上面循环已覆盖字面量）
        PaymentRefundPermissionHostPinProvider.PrepaymentMoneyPermissionNames.ShouldContain(
            "EasyAbp.PaymentService.Prepayment.Account.Manage");
        PaymentRefundPermissionHostPinProvider.PrepaymentMoneyPermissionNames.ShouldContain(
            "EasyAbp.PaymentService.Prepayment.WithdrawalRequest.Manage");
        PaymentRefundPermissionHostPinProvider.PrepaymentMoneyPermissionNames.ShouldContain(
            "EasyAbp.PaymentService.Prepayment.WithdrawalRecord.Manage");
    }
}
