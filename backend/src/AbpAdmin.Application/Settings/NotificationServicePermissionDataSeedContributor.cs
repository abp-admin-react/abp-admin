using System.Threading.Tasks;
using EasyAbp.NotificationService.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Settings;

/* 与 FileManagementPermissionDataSeedContributor 同理（注释见该文件）：
 * EasyAbp.NotificationService 是后接入的模块，其权限在首次种子之后才会被发现，
 * 这里显式把 Notification 权限授给 admin 角色。幂等，可随每次迁移安全重跑。
 * 权限常量类 NotificationServicePermissions 在模块的 Application.Contracts，
 * 故本 seeder 放在 Application 层。
 */
public class NotificationServicePermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public NotificationServicePermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
    {
        _permissionDataSeeder = permissionDataSeeder;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            NotificationServicePermissions.GetAll(),
            context.TenantId);
    }
}
