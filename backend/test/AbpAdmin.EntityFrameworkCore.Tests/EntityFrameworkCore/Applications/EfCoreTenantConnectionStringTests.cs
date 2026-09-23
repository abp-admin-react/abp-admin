using AbpAdmin.Saas;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Saas;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreTenantConnectionStringTests : TenantConnectionStringTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
