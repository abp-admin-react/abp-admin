using AbpAdmin.ScheduledJobs;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreScheduledJobAppServiceTests : ScheduledJobAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
