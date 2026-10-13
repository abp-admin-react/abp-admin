using System.Threading.Tasks;
using EasyAbp.FileManagement.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Files;

/* ABP 自带的 PermissionDataSeedContributor 只在首次种子时把"当时已定义"的权限授给 admin。
 * EasyAbp.FileManagement 是后接入的模块，其权限在首次种子之后才会被发现，
 * 导致 admin 拿不到 EasyAbp.FileManagement.File.* 权限（接口 403）。
 * 这里显式把 FileManagement 的全部 File 权限授给 admin 角色。
 * 权限常量类 FileManagementPermissions 在 EasyAbp.FileManagement.Application.Contracts，
 * 故本 seeder 放在 Application 层（已引用该包），避免 Domain 反向引用 Contracts。
 * IPermissionDataSeeder.SeedAsync 内部按 "已授予则跳过" 处理，幂等，可随每次迁移安全重跑。
 */
public class FileManagementPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public FileManagementPermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
    {
        _permissionDataSeeder = permissionDataSeeder;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            FileManagementPermissions.GetAll(),
            context.TenantId);
    }
}
