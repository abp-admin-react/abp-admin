using AbpAdmin.RateLimiting;
using AbpAdmin.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore.Domains;

/* 分区键有界化的 DI 解析锚点：契约断言在抽象基类。 */
public class EfCorePartitionKeyResolverTests : PartitionKeyResolverTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
