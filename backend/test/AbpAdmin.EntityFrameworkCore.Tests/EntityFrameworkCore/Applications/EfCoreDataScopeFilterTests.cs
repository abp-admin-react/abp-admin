using AbpAdmin.DataScopes;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreDataScopeFilterTests : DataScopeFilterTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
