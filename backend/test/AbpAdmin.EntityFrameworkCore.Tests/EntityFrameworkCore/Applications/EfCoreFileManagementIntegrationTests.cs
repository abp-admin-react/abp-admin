using AbpAdmin.Files;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreFileManagementIntegrationTests : FileManagementIntegrationTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
