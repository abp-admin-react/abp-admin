using Volo.Abp.Authorization.Permissions;
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
        var plans = group.AddPermission(TokenGatewayPermissions.Plans.Default);
        plans.AddChild(TokenGatewayPermissions.Plans.Manage);
    }
}
