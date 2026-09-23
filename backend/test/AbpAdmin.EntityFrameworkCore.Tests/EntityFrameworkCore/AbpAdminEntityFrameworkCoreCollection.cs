using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

[CollectionDefinition(AbpAdminTestConsts.CollectionDefinitionName)]
public class AbpAdminEntityFrameworkCoreCollection : ICollectionFixture<AbpAdminEntityFrameworkCoreFixture>
{

}
