using AbpAdmin.OperationLogs;
using AbpAdmin.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* OperationLogWriter 的 EF Core（SQLite）落库实现锚点：契约断言在抽象基类。 */
public class EfCoreOperationLogWriterTests : OperationLogWriterTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
