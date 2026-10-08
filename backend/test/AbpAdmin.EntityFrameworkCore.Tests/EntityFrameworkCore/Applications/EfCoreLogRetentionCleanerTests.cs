using AbpAdmin.Logging;
using AbpAdmin.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* 日志保留期清理的 EF Core（SQLite）落库实现锚点：契约断言在抽象基类。 */
public class EfCoreLogRetentionCleanerTests : LogRetentionCleanerTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
