using AbpAdmin.Profile;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreProfileAvatarAppServiceTests : ProfileAvatarAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
