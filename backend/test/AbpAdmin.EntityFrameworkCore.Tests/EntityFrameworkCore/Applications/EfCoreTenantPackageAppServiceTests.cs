using AbpAdmin.Tenants;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreTenantPackageAppServiceTests : TenantPackageAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
