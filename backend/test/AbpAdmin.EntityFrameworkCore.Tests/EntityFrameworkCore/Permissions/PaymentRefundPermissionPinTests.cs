using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Permissions;

using AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 回归锁：包侧退款权限必须被钉到 Host。反编译定案：EasyAbp PaymentService 2.21.0
/// 的 PaymentServicePermissionDefinitionProvider 以 MultiTenancySides.Both 定义 Refund 树，
/// 租户创建种子随后授予每个租户 admin——包 HttpApi 的 RefundController 因此成为绕过
/// repo 侧 IPaymentRefundGuard 的退款平行走道。本锁断言 PostDefine 改写生效：
/// 租户侧定义消失 ⇒ 种子不会再授予新租户。
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

        // 对照：包的 Payment 管理权限保持包原生双侧定义（本修复只针对退款平行走道，
        // 不扩大改动面；Payment.Manage 的租户面如需收紧另立决策）。
        var payment = await manager.GetAsync("EasyAbp.PaymentService.Payment.Manage");
        payment.MultiTenancySide.ShouldNotBe(MultiTenancySides.Host);
    }
}
