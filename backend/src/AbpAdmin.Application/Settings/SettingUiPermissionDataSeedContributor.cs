using System.Threading.Tasks;
using AbpAdmin.Permissions;
using EasyAbp.Abp.SettingUi.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Settings;

/* ABP 自带的 PermissionDataSeedContributor 只在首次种子时把"当时已定义"的权限授给 admin。
 * EasyAbp.Abp.SettingUi 是后接入的模块，其权限在首次种子之后才会被发现，
 * 导致 admin 拿不到 SettingUi.ShowSettingPage 权限（接口 403）。
 * 这里显式把 SettingUi 的 ShowSettingPage 权限授给 admin 角色。
 * IPermissionDataSeeder.SeedAsync 内部按 "已授予则跳过" 处理，幂等，可随每次迁移安全重跑。
 */
public class SettingUiPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public SettingUiPermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
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
                SettingUiPermissions.ShowSettingPage,
                // 读写分离（模块五收口）：写权限与读权限一起授给 admin，
                // 否则收口后 admin 能看设置页但保存/重置 403。
                AbpAdminPermissions.SettingUi.Update,
            },
            context.TenantId);
    }
}
