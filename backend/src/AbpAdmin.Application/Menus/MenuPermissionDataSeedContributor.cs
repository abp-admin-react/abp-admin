using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Menus;

/// <summary>
/// 菜单管理是后接入的模块，ABP 自带的 PermissionDataSeedContributor 只在首次种子时
/// 授权"当时已定义"的权限——这里显式把 AbpAdmin.Menus.* 授给 admin（host 与每个租户各一份）。
/// 仅负责菜单模块自身的权限；租户套餐/令牌管理的权限由各自模块的贡献者播种。
/// </summary>
public class MenuPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;

    public MenuPermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
    {
        _permissionDataSeeder = permissionDataSeeder;
    }

    [UnitOfWork]
    public virtual Task SeedAsync(DataSeedContext context)
    {
        return _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            new[]
            {
                AbpAdminPermissions.Menus.Default,
                AbpAdminPermissions.Menus.Create,
                AbpAdminPermissions.Menus.Update,
                AbpAdminPermissions.Menus.Delete,
                AbpAdminPermissions.Menus.AssignRoles
            },
            context.TenantId);
    }
}
