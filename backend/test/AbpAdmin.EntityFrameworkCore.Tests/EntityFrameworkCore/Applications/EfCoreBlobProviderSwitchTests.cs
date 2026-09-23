using AbpAdmin.Blobs;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreBlobProviderSwitchTests : BlobProviderSwitchTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
