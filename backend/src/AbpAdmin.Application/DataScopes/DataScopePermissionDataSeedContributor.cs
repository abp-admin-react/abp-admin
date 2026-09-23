using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.DataScopes;

/* ABP 自带的 PermissionDataSeedContributor 只在首次种子时把"当时已定义"的权限授给 admin。
 * DataScopes 是后接入的模块，其权限在首次种子之后才会被发现，
 * 导致 admin 拿不到 AbpAdmin.DataScopes.* 权限（接口 403）。
 * 这里显式把 DataScopes 的全部权限授给 admin 角色。
 * IPermissionDataSeeder.SeedAsync 内部按 "已授予则跳过" 处理，幂等，可随每次迁移安全重跑。
 */
public class DataScopePermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public DataScopePermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
    {
        _permissionDataSeeder = permissionDataSeeder;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            new[]
            {
                AbpAdminPermissions.DataScopes.Default,
                AbpAdminPermissions.DataScopes.Manage,
                AbpAdminPermissions.DataScopes.Demo
            },
            context.TenantId);
    }
}
