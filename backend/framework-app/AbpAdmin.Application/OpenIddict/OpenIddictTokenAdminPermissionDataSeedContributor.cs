using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 令牌/授权管理是后接入的 Host 专属模块：显式把 AbpAdmin.OpenIddict.Tokens.* 授给
/// host 的 admin 角色（租户侧不授）。
/// </summary>
public class OpenIddictTokenAdminPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public OpenIddictTokenAdminPermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
    {
        _permissionDataSeeder = permissionDataSeeder;
    }

    [UnitOfWork]
    public virtual Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId != null)
        {
            return Task.CompletedTask;
        }

        return _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            new[]
            {
                AbpAdminPermissions.OpenIddict.Tokens.Default,
                AbpAdminPermissions.OpenIddict.Tokens.Revoke,
                AbpAdminPermissions.OpenIddict.Tokens.Prune
            },
            context.TenantId);
    }
}
