using AbpAdmin.Localization;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreLanguageAppServiceTests : LanguageAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
