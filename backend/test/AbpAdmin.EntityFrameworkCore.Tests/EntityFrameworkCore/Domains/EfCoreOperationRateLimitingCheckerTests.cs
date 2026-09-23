using AbpAdmin.RateLimiting;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreOperationRateLimitingCheckerTests : OperationRateLimitingCheckerTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
