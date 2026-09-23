using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* OperationLogAppService 查询的 EF Core（SQLite）锚点：契约断言在抽象基类。 */
public class EfCoreOperationLogAppServiceTests : OperationLogAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
