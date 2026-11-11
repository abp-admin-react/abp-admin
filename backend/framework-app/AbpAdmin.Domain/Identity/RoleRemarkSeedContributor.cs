using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Identity;

public class RoleRemarkSeedContributor(
    IIdentityRoleRepository roles,
    ICurrentTenant currentTenant) : IDataSeedContributor, ITransientDependency
{
    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        using (currentTenant.Change(context.TenantId))
        {
            var role = await roles.FindByNormalizedNameAsync(AbpRoleConsts.AdminRoleName.ToUpperInvariant());
            if (role is { IsStatic: true } && role.GetProperty<string>(RoleRemarkConsts.PropertyName) is null)
            {
                role.SetProperty(RoleRemarkConsts.PropertyName, "系统管理员，维护所属范围的账号、权限和系统配置。");
                await roles.UpdateAsync(role);
            }
        }
    }
}
