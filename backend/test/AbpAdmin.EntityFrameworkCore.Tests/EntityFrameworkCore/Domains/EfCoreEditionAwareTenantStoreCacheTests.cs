using AbpAdmin.Tenants;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreEditionAwareTenantStoreCacheTests : EditionAwareTenantStoreCacheTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
