using AbpAdmin.AuditLogs;
using AbpAdmin.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* 错误日志「已处理」工作流的 EF Core（SQLite）实现锚点：契约断言在抽象基类。 */
public class EfCoreAuditLogHandlingTests : AuditLogHandlingTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
