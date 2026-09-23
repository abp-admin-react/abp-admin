using AbpAdmin.DataScopes;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreRoleDataScopeAppServiceTests : RoleDataScopeAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
