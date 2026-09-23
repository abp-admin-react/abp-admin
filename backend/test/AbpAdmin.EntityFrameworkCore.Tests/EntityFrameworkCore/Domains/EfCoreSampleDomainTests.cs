using AbpAdmin.Samples;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
