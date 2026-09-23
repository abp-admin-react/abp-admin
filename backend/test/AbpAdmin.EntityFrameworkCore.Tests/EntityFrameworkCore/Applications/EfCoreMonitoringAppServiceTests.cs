using AbpAdmin.Monitoring;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreMonitoringAppServiceTests : MonitoringAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
