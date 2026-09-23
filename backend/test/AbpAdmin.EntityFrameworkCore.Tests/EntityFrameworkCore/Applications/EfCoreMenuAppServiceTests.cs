using AbpAdmin.Menus;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreMenuAppServiceTests : MenuAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
