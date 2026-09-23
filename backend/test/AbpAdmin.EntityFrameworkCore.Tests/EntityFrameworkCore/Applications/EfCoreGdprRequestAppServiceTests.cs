using AbpAdmin.Gdpr;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreGdprRequestAppServiceTests : GdprRequestAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
