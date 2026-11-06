using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Configuration;
using AbpAdmin.Biz.Template.Permissions;
using Microsoft.Extensions.Options;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Roles;
using Volo.Abp.Uow;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 客户错误样例权限种子：只在 <see cref="BizTemplateOptions.EnableClientErrorSample"/> 打开时
/// 把 <see cref="BizTemplatePermissions.ClientErrorSample.Default"/> 授给 admin。
/// ABP 自带 PermissionDataSeedContributor 只在首次种子把当时已定义的权限授给 admin，
/// 本权限是后加的，存量库不会自动补授——不种则联调页 403。
/// IPermissionDataSeeder.SeedAsync 内部已授予则跳过，幂等。
/// </summary>
public class BizTemplateClientErrorSamplePermissionDataSeedContributor
    : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly BizTemplateOptions _bizTemplateOptions;

    public BizTemplateClientErrorSamplePermissionDataSeedContributor(
        IPermissionDataSeeder permissionDataSeeder,
        IOptions<BizTemplateOptions> bizTemplateOptions)
    {
        _permissionDataSeeder = permissionDataSeeder;
        _bizTemplateOptions = bizTemplateOptions.Value;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (!_bizTemplateOptions.EnableClientErrorSample)
        {
            return;
        }

        await _permissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            AbpRoleConsts.AdminRoleName,
            [BizTemplatePermissions.ClientErrorSample.Default],
            context.TenantId);
    }
}
