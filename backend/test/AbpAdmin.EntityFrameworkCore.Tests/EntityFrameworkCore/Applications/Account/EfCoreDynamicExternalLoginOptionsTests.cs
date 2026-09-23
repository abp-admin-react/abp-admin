using AbpAdmin.Account;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Account;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreDynamicExternalLoginOptionsTests : DynamicExternalLoginOptionsTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
