using AbpAdmin.Identity;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreUserImportExportTests : UserImportExportTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
