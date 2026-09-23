using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.RateLimiting;

/* T2.5 操作限流拦截器集成测试。
 * 覆盖验收标准第 1156 行要求的场景：
 * - 特性在应用服务上真的生效（virtual 方法）
 * - 非 virtual 方法不生效（负向用例）
 * - 一个方法标两个 [RateLimitingParameter] 时抛异常（编码错误）
 * - 方法有参数但未解析到分区值时，所有用户共用一个计数
 * - IHasOperationRateLimitingParameter 接口解析参数
 * - 类级特性对所有 virtual 方法生效；方法级特性覆盖类级
 *
 * 策略名是编译期常量，测试通过 Configure 在每个用例中重建策略
 * （AddPolicy 同名覆盖），并用随机分区值隔离计数。
 */
public abstract class OperationRateLimitingInterceptorTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly RateLimitingTestAppService _service;
    private readonly ClassLevelRateLimitingTestAppService _classLevelService;

    /// <summary>直接修改单例 Options（IOptions.Value 是同一实例，修改后全局生效）。</summary>
    private AbpAdminOperationRateLimitingOptions RateLimitingOptions =>
        GetRequiredService<Microsoft.Extensions.Options.IOptions<AbpAdminOperationRateLimitingOptions>>().Value;

    protected OperationRateLimitingInterceptorTests()
    {
        _service = GetRequiredService<RateLimitingTestAppService>();
        _classLevelService = GetRequiredService<ClassLevelRateLimitingTestAppService>();
    }

    /// <summary>
    /// 重建占位策略（同名覆盖），maxCount 由测试指定。
    /// </summary>
    private void ResetPlaceholderPolicy(int maxCount)
    {
        RateLimitingOptions.AddPolicy(InterceptorTestPolicies.Placeholder, p => p
            .AddRule(r => r.PartitionByParameter()
                .WithFixedWindow(TimeSpan.FromMinutes(10), maxCount)));
    }

    private void ResetOverridePolicy(int maxCount)
    {
        RateLimitingOptions.AddPolicy(InterceptorTestPolicies.PlaceholderOverride, p => p
            .AddRule(r => r.PartitionByParameter()
                .WithFixedWindow(TimeSpan.FromMinutes(10), maxCount)));
    }

    [Fact]
    public async Task Attribute_Should_Work_On_Virtual_Method()
    {
        ResetPlaceholderPolicy(2);
        var key = Guid.NewGuid().ToString("N");

        // 前 2 次通过
        await _service.VirtualLimitedMethodAsync(key);
        await _service.VirtualLimitedMethodAsync(key);

        // 第 3 次被拦截器拒绝
        var ex = await Should.ThrowAsync<AbpAdminOperationRateLimitingException>(
            _service.VirtualLimitedMethodAsync(key));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.RateLimiting.OperationRateLimitExceeded);

        // 不同分区不受影响
        await _service.VirtualLimitedMethodAsync(Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task Attribute_Should_Not_Work_On_NonVirtual_Method()
    {
        // 负向用例：非 virtual 方法不生效（ABP 动态代理基于继承，无法拦截非 virtual 方法）
        ResetPlaceholderPolicy(1);
        var key = Guid.NewGuid().ToString("N");

        // 调用 3 次都通过（拦截器不生效，特性静默失效）
        await _service.NonVirtualLimitedMethodAsync(key);
        await _service.NonVirtualLimitedMethodAsync(key);
        await _service.NonVirtualLimitedMethodAsync(key);
    }

    [Fact]
    public async Task Multiple_RateLimitingParameter_Should_Throw()
    {
        // 编码错误：一个方法标两个 [RateLimitingParameter] 时抛 AbpException
        ResetPlaceholderPolicy(100);

        var ex = await Should.ThrowAsync<AbpException>(
            _service.MultiMarkedParameterMethodAsync("a", "b"));
        ex.Message.ShouldContain("RateLimitingParameter");
    }

    [Fact]
    public async Task Interface_Parameter_Should_Be_Resolved()
    {
        ResetPlaceholderPolicy(1);
        var key = Guid.NewGuid().ToString("N");

        // 参数实现 IHasOperationRateLimitingParameter，自动解析分区值
        await _service.InterfaceParameterMethodAsync(new TestPartitionInput { Value = key });
        await Should.ThrowAsync<AbpAdminOperationRateLimitingException>(
            _service.InterfaceParameterMethodAsync(new TestPartitionInput { Value = key }));

        // 不同分区值不受影响
        await _service.InterfaceParameterMethodAsync(new TestPartitionInput { Value = Guid.NewGuid().ToString("N") });
    }

    [Fact]
    public async Task Unresolvable_Parameter_Should_Share_Null_Partition()
    {
        // 方法有参数但未解析到分区值时，parameter = null，所有用户共用一个计数
        // 重建 NullPartition 策略（同名覆盖，隔离其他测试的计数）
        RateLimitingOptions.AddPolicy(InterceptorTestPolicies.NullPartition, p => p
            .AddRule(r => r.PartitionByParameter()
                .WithFixedWindow(TimeSpan.FromMinutes(10), 1)));

        var service = GetRequiredService<NullPartitionTestAppService>();

        // int 参数无法解析为分区值 → parameter = null → 所有调用共用 "null" 分区
        await service.UnresolvableParameterMethodAsync(12345);
        await Should.ThrowAsync<AbpAdminOperationRateLimitingException>(
            service.UnresolvableParameterMethodAsync(67890)); // 不同参数值也被拒（共用 null 分区）
    }

    [Fact]
    public async Task Class_Level_Attribute_Should_Apply_To_All_Virtual_Methods()
    {
        ResetPlaceholderPolicy(1);
        var key = Guid.NewGuid().ToString("N");

        // 类级特性对方法一生效
        await _classLevelService.MethodOneAsync(key);
        await Should.ThrowAsync<AbpAdminOperationRateLimitingException>(
            _classLevelService.MethodOneAsync(key));

        // 类级特性对方法二也生效（同一策略同一分区，计数共享）
        await Should.ThrowAsync<AbpAdminOperationRateLimitingException>(
            _classLevelService.MethodTwoAsync(key));
    }

    [Fact]
    public async Task Method_Level_Attribute_Should_Override_Class_Level()
    {
        // 类级策略 1 次限额，方法级策略 100 次限额
        ResetPlaceholderPolicy(1);
        ResetOverridePolicy(100);
        var key = Guid.NewGuid().ToString("N");

        // 方法级特性覆盖类级：MethodThree 用 Override 策略（100 次限额），可以连续调用
        await _classLevelService.MethodThreeAsync(key);
        await _classLevelService.MethodThreeAsync(key);
        await _classLevelService.MethodThreeAsync(key);

        // 但 MethodOne 仍受类级策略限制（1 次限额）
        await _classLevelService.MethodOneAsync(key);
        await Should.ThrowAsync<AbpAdminOperationRateLimitingException>(
            _classLevelService.MethodOneAsync(key));
    }
}

/// <summary>
/// 策略名常量（编译期）。
/// </summary>
public static class InterceptorTestPolicies
{
    public const string Placeholder = "InterceptorTest_Placeholder";
    public const string PlaceholderOverride = "InterceptorTest_PlaceholderOverride";
    public const string NullPartition = "InterceptorTest_NullPartition";
}

/// <summary>
/// 测试用应用服务：方法级特性。
/// </summary>
public class RateLimitingTestAppService : ApplicationService
{
    [OperationRateLimiting(InterceptorTestPolicies.Placeholder)]
    public virtual Task VirtualLimitedMethodAsync([RateLimitingParameter] string userKey)
    {
        return Task.CompletedTask;
    }

    [OperationRateLimiting(InterceptorTestPolicies.Placeholder)]
    public Task NonVirtualLimitedMethodAsync([RateLimitingParameter] string userKey)
    {
        return Task.CompletedTask;
    }

    [OperationRateLimiting(InterceptorTestPolicies.Placeholder)]
    public virtual Task MultiMarkedParameterMethodAsync(
        [RateLimitingParameter] string first,
        [RateLimitingParameter] string second)
    {
        return Task.CompletedTask;
    }

    [OperationRateLimiting(InterceptorTestPolicies.Placeholder)]
    public virtual Task InterfaceParameterMethodAsync(TestPartitionInput input)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// 测试用应用服务：null 分区（参数无法解析）隔离测试。
/// </summary>
public class NullPartitionTestAppService : ApplicationService
{
    [OperationRateLimiting(InterceptorTestPolicies.NullPartition)]
    public virtual Task UnresolvableParameterMethodAsync(int number)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// 测试用应用服务：类级特性。
/// </summary>
[OperationRateLimiting(InterceptorTestPolicies.Placeholder)]
public class ClassLevelRateLimitingTestAppService : ApplicationService
{
    public virtual Task MethodOneAsync([RateLimitingParameter] string userKey)
    {
        return Task.CompletedTask;
    }

    public virtual Task MethodTwoAsync([RateLimitingParameter] string userKey)
    {
        return Task.CompletedTask;
    }

    [OperationRateLimiting(InterceptorTestPolicies.PlaceholderOverride)]
    public virtual Task MethodThreeAsync([RateLimitingParameter] string userKey)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// 测试用分区参数输入。
/// </summary>
public class TestPartitionInput : IHasOperationRateLimitingParameter
{
    public string? Value { get; set; }

    public string? GetPartitionParameter() => Value;
}
