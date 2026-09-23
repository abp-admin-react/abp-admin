using AbpAdmin.DataScopes;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreCacheNameCollisionTests : CacheNameCollisionTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
