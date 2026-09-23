using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.IpRegions;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/* IP 归属地解析的 EF Core 测试套件锚点（共享同一 SQLite collection）。 */
public class EfCoreIpLocationResolverTests : IpLocationResolverTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
