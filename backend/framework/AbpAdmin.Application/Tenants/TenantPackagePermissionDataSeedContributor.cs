using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户套餐是后接入的 Host 专属模块：显式把 AbpAdmin.TenantPackages.* 授给 host 的
/// admin 角色（租户侧不授——权限本身是 MultiTenancySides.Host，租户种子上下文直接跳过）。
/// </summary>
public class TenantPackagePermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public TenantPackagePermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
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
                AbpAdminPermissions.TenantPackages.Default,
                AbpAdminPermissions.TenantPackages.Create,
                AbpAdminPermissions.TenantPackages.Update,
                AbpAdminPermissions.TenantPackages.Delete
            },
            context.TenantId);
    }
}
