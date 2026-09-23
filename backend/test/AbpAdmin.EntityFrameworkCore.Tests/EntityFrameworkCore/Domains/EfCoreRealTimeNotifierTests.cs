using AbpAdmin.RealTime;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreRealTimeNotifierTests : RealTimeNotifierTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
