using AbpAdmin.AuditLogs;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreAuditLogStatisticsTests : AuditLogStatisticsTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
