using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;

namespace AbpAdmin.TokenGateway;

/// <summary>
/// TokenGateway 权限定义。刻意不带本地化显示名：本模块没有本地化资源，管理面就两个
/// 权限位，UI 回显权限键已够用；要中文名时再像 Ledger 一样接资源（避免为两个键引入整套路由）。
/// </summary>
public class TokenGatewayPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(TokenGatewayPermissions.GroupName);
        // TgPlans 无租户列、是宿主全局表——缺省侧（两侧可授）会被
        // 建租户种子的框架 PermissionDataSeedContributor 授给租户 admin（反编译 10.7.0 实证），
        // 租户 admin 即可改写全平台价目。与 Collection.Network.Admin/Withdraw.Admin 同口径锁 Host。
        var plans = group.AddPermission(TokenGatewayPermissions.Plans.Default,
            multiTenancySide: MultiTenancySides.Host);
        plans.AddChild(TokenGatewayPermissions.Plans.Manage,
            multiTenancySide: MultiTenancySides.Host);
    }
}
