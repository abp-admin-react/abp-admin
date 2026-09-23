using AbpAdmin.OpenIddict;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreOpenIddictTokenAdminAppServiceTests : OpenIddictTokenAdminAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
