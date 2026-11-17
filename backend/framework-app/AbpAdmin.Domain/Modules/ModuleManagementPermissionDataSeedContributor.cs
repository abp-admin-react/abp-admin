using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;

namespace AbpAdmin.Modules;

// Domain placement keeps this additive permission upgrade available in both Host and DbMigrator.
public sealed class ModuleManagementPermissionDataSeedContributor(IPermissionDataSeeder seeder) : IDataSeedContributor, ITransientDependency
{
    public Task SeedAsync(DataSeedContext context) => context.TenantId.HasValue ? Task.CompletedTask :
        seeder.SeedAsync(RolePermissionValueProvider.ProviderName, AbpRoleConsts.AdminRoleName,
            [DeploymentModulePermissions.Default, DeploymentModulePermissions.Update], null);
}
