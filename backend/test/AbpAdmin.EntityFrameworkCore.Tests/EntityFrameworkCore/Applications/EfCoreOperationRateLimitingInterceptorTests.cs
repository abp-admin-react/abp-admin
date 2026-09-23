using AbpAdmin.RateLimiting;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreOperationRateLimitingInterceptorTests : OperationRateLimitingInterceptorTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
