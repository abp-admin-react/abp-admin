using AbpAdmin.Posts;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCorePostAppServiceTests : PostAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
