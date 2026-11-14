using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Permissions;

/// <summary>为宿主 admin 角色补授新增的收款权限。</summary>
public class CollectionPaymentPermissionDataSeedContributor(IPermissionDataSeeder permissionDataSeeder)
    : IDataSeedContributor, ITransientDependency
{
    [UnitOfWork]
    public virtual Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId != null)
        {
            return Task.CompletedTask;
        }

        return permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            [CollectionPermissions.Payments.View, CollectionPermissions.Payments.Reconcile,
                CollectionPermissions.Payments.Refund,
                CollectionPermissions.BalanceAdjustments.View, CollectionPermissions.BalanceAdjustments.Create,
                CollectionPermissions.BalanceAdjustments.Approve, CollectionPermissions.BalanceAdjustments.Reject],
            context.TenantId);
    }
}
