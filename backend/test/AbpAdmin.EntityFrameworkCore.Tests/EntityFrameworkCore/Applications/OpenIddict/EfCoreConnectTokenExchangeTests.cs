using AbpAdmin.OpenIddict;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.OpenIddict;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreConnectTokenExchangeTests : ConnectTokenExchangeTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
