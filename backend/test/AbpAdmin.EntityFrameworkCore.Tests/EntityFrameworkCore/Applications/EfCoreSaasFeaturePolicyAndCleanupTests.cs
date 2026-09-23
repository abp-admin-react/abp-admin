using AbpAdmin.Saas;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Saas;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreSaasFeaturePolicyAndCleanupTests : SaasFeaturePolicyAndCleanupTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
