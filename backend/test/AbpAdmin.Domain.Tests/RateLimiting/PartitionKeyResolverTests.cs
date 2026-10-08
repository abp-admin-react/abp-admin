using System.Threading.Tasks;
using AbpAdmin.RateLimiting.PartitionKeyResolvers;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.RateLimiting;

/* 分区键有界化契约测试（EFCore 锚点见 EfCore 版）：
 * 登录输入未经验证直通分区键 → 计数缓存键，无界输入是微软限流文档点名的内存 DoS
 * 反模式（"partitioning on unbounded user-controlled input can exhaust memory"）。
 * 三个用户输入解析器（Email/Parameter/PhoneNumber）统一截断到 MaxLength（256，对齐
 * 用户名上限——合法输入永不触碰截断，攻击输入合并进同桶自锁，碰撞只对自己不利）；
 * 服务端可控解析器（IP/CurrentUser/Tenant）天然有界，不在此列。
 */
public abstract class PartitionKeyResolverTests<TStartupModule> : AbpAdminDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task Parameter_Resolver_Truncates_Overlong_Input_To_256()
    {
        var resolver = GetRequiredService<ParameterOperationRateLimitingPartitionKeyResolver>();

        var key = await resolver.ResolveAsync(new OperationRateLimitingContext
        {
            Parameter = new string('A', 10000),
        });

        key.ShouldNotBeNull();
        key.Length.ShouldBe(OperationRateLimitingPartitionKeys.MaxLength);
    }

    [Fact]
    public async Task Parameter_Resolver_Truncates_Exactly_One_Over_Boundary()
    {
        // 257 = MaxLength+1：value[..MaxLength] 的精确差一边界（Off-by-one 陷阱）
        var resolver = GetRequiredService<ParameterOperationRateLimitingPartitionKeyResolver>();

        var key = await resolver.ResolveAsync(new OperationRateLimitingContext
        {
            Parameter = new string('B', OperationRateLimitingPartitionKeys.MaxLength + 1),
        });

        key.ShouldNotBeNull();
        key.Length.ShouldBe(OperationRateLimitingPartitionKeys.MaxLength);
        key.ShouldBe(new string('B', OperationRateLimitingPartitionKeys.MaxLength));
    }

    [Fact]
    public async Task Parameter_Resolver_Keeps_Bounded_Input_Intact()
    {
        var resolver = GetRequiredService<ParameterOperationRateLimitingPartitionKeyResolver>();
        var exact = new string('B', OperationRateLimitingPartitionKeys.MaxLength);

        (await resolver.ResolveAsync(new OperationRateLimitingContext { Parameter = exact })).ShouldBe(exact);
        (await resolver.ResolveAsync(new OperationRateLimitingContext { Parameter = "short" })).ShouldBe("short");
        (await resolver.ResolveAsync(new OperationRateLimitingContext { Parameter = null })).ShouldBeNull();
    }

    [Fact]
    public async Task Email_Resolver_Uppercases_Then_Truncates()
    {
        var resolver = GetRequiredService<EmailOperationRateLimitingPartitionKeyResolver>();

        var key = await resolver.ResolveAsync(new OperationRateLimitingContext
        {
            Parameter = new string('a', 300),
        });

        key.ShouldNotBeNull();
        key.Length.ShouldBe(OperationRateLimitingPartitionKeys.MaxLength);
        key.ShouldContain('A');
        key.ShouldNotContain('a');
    }

    [Fact]
    public async Task PhoneNumber_Resolver_Normalizes_Then_Truncates()
    {
        var resolver = GetRequiredService<PhoneNumberOperationRateLimitingPartitionKeyResolver>();

        var normalized = await resolver.ResolveAsync(new OperationRateLimitingContext
        {
            Parameter = "138-0013 (8000)",
        });
        normalized.ShouldBe("13800138000");

        var key = await resolver.ResolveAsync(new OperationRateLimitingContext
        {
            Parameter = new string('1', 300),
        });
        key.ShouldNotBeNull();
        key.Length.ShouldBe(OperationRateLimitingPartitionKeys.MaxLength);
    }
}
