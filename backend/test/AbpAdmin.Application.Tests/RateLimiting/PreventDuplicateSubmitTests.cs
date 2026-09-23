using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Posts;
using AbpAdmin.RateLimiting;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.RateLimiting;

/* 防重复提交测试：窗口语义对标 RuoYi @RepeatSubmit，抢占/释放机制对标 ruoyi-vue-pro @Idempotent。
 * 以 PostAppService.CreateAsync 为参照实现 + PreventDuplicateSubmitTestService 定向夹具。
 * 覆盖：同参数窗口内二次提交被拒（错误码 + 窗口秒数值）、不同参数不受影响、
 * 业务异常默认立即释放窗口（可马上重试，且首抛错误码被断言）、
 * DeleteKeyOnException=false 回到"进入即标记"、
 * Argument 作用域只认业务键参数（含 null 业务键当场失败、ArgumentName 反向误配当场失败）、
 * Global 作用域跨用户互斥 / User 作用域跨用户放行（端到端走拦截器，ICurrentPrincipalAccessor 切换身份）、
 * Global 指纹不含用户 / User 指纹含用户（纯函数断言）、
 * 并发首拍只放一个（Task.Run 门闩并发，回归 check-then-set 竞态）、
 * 锁被外部持有时抛基础设施 AbpException 而非业务 DuplicateSubmit、
 * 窗口 TTL 到期后放行（真实等待 2.5s）。
 * 引擎级缓存跨用例存活，所有用例都用随机参数隔离；
 * 锁超时与 TTL 用例各引入数秒真实等待（5s / 2.5s），是套件仅有的两个慢用例。
 */
public abstract class PreventDuplicateSubmitTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string SimulatedFailureCode = "AbpAdmin:Test:SimulatedFailure";

    private readonly IPostAppService _postAppService;
    private readonly PreventDuplicateSubmitTestService _testService;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;

    protected PreventDuplicateSubmitTests()
    {
        _postAppService = GetRequiredService<IPostAppService>();
        _testService = GetRequiredService<PreventDuplicateSubmitTestService>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private static string RandomArg() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>以指定用户身份执行操作（与 GdprRequestAppServiceTests 同款跨用户测试手法）。</summary>
    private IDisposable ChangeCurrentUser(Guid userId) =>
        _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, "user-" + userId.ToString("N")[..8]),
        })));

    private static CreatePostDto CreateInput(string suffix) => new()
    {
        Name = "防重岗位" + suffix,
        Code = "DUP" + suffix,
        SortOrder = 0,
        Status = PostStatusEnum.Enabled
    };

    [Fact]
    public async Task Same_Arguments_Second_Submit_Should_Be_Rejected()
    {
        var input = CreateInput(RandomArg());

        var first = await _postAppService.CreateAsync(input);
        first.ShouldNotBeNull();

        var second = await Should.ThrowAsync<BusinessException>(() => _postAppService.CreateAsync(input));
        second.Code.ShouldBe(AbpAdminDomainErrorCodes.RateLimiting.DuplicateSubmit);
        // 窗口秒数值与特性配置（默认 5）一致，防止报给客户端的等待时间与实际窗口脱节
        second.Data["IntervalSeconds"].ShouldBe(5);
    }

    [Fact]
    public async Task Different_Arguments_Should_Not_Interfere()
    {
        var first = await _postAppService.CreateAsync(CreateInput(RandomArg()));
        var second = await _postAppService.CreateAsync(CreateInput(RandomArg()));

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        first.Id.ShouldNotBe(second.Id);
    }

    [Fact]
    public async Task Exception_Should_Release_Window_Immediately()
    {
        var arg = RandomArg();

        // 第一次业务失败（模拟异常），窗口标记应立即释放；错误码断言防止被误挂的 DuplicateSubmit 混过
        var first = await Should.ThrowAsync<BusinessException>(() => _testService.FailOnceAsync(arg));
        first.Code.ShouldBe(SimulatedFailureCode);

        // 同参数立刻重试应放行，而不是被窗口拒绝（对标 yudao deleteKeyWhenException）
        await _testService.FailOnceAsync(arg);
    }

    [Fact]
    public async Task Exception_Should_Keep_Window_When_DeleteKeyOnException_False()
    {
        var arg = RandomArg();

        var first = await Should.ThrowAsync<BusinessException>(() => _testService.FailAlwaysKeepsWindowAsync(arg));
        first.Code.ShouldBe(SimulatedFailureCode);

        // 关闭异常释放后回到 RuoYi "进入即标记"：失败也占满窗口
        var second = await Should.ThrowAsync<BusinessException>(() => _testService.FailAlwaysKeepsWindowAsync(arg));
        second.Code.ShouldBe(AbpAdminDomainErrorCodes.RateLimiting.DuplicateSubmit);
    }

    [Fact]
    public async Task Argument_Scope_Should_Ignore_Other_Parameters()
    {
        var orderNo = RandomArg();

        // 同业务键、不同其他参数 → 拒绝
        await _testService.ByOrderNoAsync(orderNo, "first");
        var duplicate = await Should.ThrowAsync<BusinessException>(
            () => _testService.ByOrderNoAsync(orderNo, "second"));
        duplicate.Code.ShouldBe(AbpAdminDomainErrorCodes.RateLimiting.DuplicateSubmit);

        // 不同业务键、相同其他参数 → 放行
        await _testService.ByOrderNoAsync(RandomArg(), "first");
    }

    [Fact]
    public async Task Global_Scope_Should_Reject_Across_Users_End_To_End()
    {
        var arg = RandomArg();

        await _testService.GlobalOkAsync(arg);

        // Global 不含用户段：换一个用户同参数提交仍被拒（端到端走拦截器，验证 Scope 真被读取）
        var otherUser = Guid.NewGuid();
        using (ChangeCurrentUser(otherUser))
        {
            var rejected = await Should.ThrowAsync<BusinessException>(() => _testService.GlobalOkAsync(arg));
            rejected.Code.ShouldBe(AbpAdminDomainErrorCodes.RateLimiting.DuplicateSubmit);
        }
    }

    [Fact]
    public async Task User_Scope_Should_Allow_Across_Users_End_To_End()
    {
        var arg = RandomArg();

        await _testService.UserOkAsync(arg);

        // User 指纹含用户段：另一个用户同参数是独立窗口，应放行
        using (ChangeCurrentUser(Guid.NewGuid()))
        {
            await _testService.UserOkAsync(arg);
        }
    }

    [Fact]
    public async Task Concurrent_First_Beat_Should_Allow_Only_One()
    {
        var arg = RandomArg();
        const int attempts = 8;
        // 门闩让所有任务同时起跑，Task.Run 提供真并发（原 Select+WhenAll 的 lambda 是顺序枚举，
        // 同步完成的管道下根本不交叠，旧竞态实现也能通过——那样就没有回归价值了）
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, attempts)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                try
                {
                    await _testService.OkAsync(arg);
                    return "ok";
                }
                catch (BusinessException ex) when (ex.Code == AbpAdminDomainErrorCodes.RateLimiting.DuplicateSubmit)
                {
                    return "rejected";
                }
            }))
            .ToArray();
        gate.SetResult();

        var outcomes = await Task.WhenAll(tasks);

        // 原子抢占回归：并发相同请求恰好一个放行（原 check-then-set 实现可能全放行）
        outcomes.Count(o => o == "ok").ShouldBe(1);
        outcomes.Count(o => o == "rejected").ShouldBe(attempts - 1);
    }

    [Fact]
    public async Task Lock_Timeout_Should_Throw_Infra_Exception_Not_DuplicateSubmit()
    {
        var arg = RandomArg();
        var method = typeof(PreventDuplicateSubmitTestService).GetMethod(
            nameof(PreventDuplicateSubmitTestService.OkAsync))!;
        var key = PreventDuplicateSubmitInterceptor.CalculateCacheKey(
            GetRequiredService<ICurrentUser>().Id?.ToString(), method, new object?[] { arg });
        var lockKey = $"{key}:lock:{GetRequiredService<ICurrentTenant>().Id?.ToString() ?? "host"}";

        // 外部长期持有同指纹的锁：拦截器等锁 LockTimeout(5s) 后应抛基础设施异常，
        // 绝不能误报成业务拒绝 DuplicateSubmit（基础设施故障与业务防重的口径区分）
        await using (await GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(lockKey, TimeSpan.FromSeconds(30)))
        {
            var ex = await Should.ThrowAsync<AbpException>(() => _testService.OkAsync(arg));
            ex.ShouldBeOfType(typeof(AbpException));
        }
    }

    [Fact]
    public async Task Window_Should_Expire_After_Interval()
    {
        var arg = RandomArg();

        await _testService.OkAsync(arg);
        await Should.ThrowAsync<BusinessException>(() => _testService.OkAsync(arg));

        // 夹具窗口 2 秒：TTL 到期后同参数放行（窗口必须会结束——这是防重语义的另一半）
        await Task.Delay(2500);
        await _testService.OkAsync(arg);
    }

    [Fact]
    public void Global_Scope_Key_Should_Ignore_User_While_User_Scope_Should_Not()
    {
        var method = typeof(PreventDuplicateSubmitTestService).GetMethod(nameof(PreventDuplicateSubmitTestService.OkAsync))!;
        var args = new object?[] { "shared" };

        var userKey1 = PreventDuplicateSubmitInterceptor.CalculateCacheKey("user-1", method, args);
        var userKey2 = PreventDuplicateSubmitInterceptor.CalculateCacheKey("user-2", method, args);
        userKey1.ShouldNotBe(userKey2);

        var globalKey1 = PreventDuplicateSubmitInterceptor.CalculateCacheKey(
            "user-1", method, args, PreventDuplicateSubmitScope.Global);
        var globalKey2 = PreventDuplicateSubmitInterceptor.CalculateCacheKey(
            "user-2", method, args, PreventDuplicateSubmitScope.Global);
        globalKey1.ShouldBe(globalKey2);
    }

    [Fact]
    public async Task Argument_Scope_Misconfiguration_Should_Fail_Fast()
    {
        // ArgumentName 缺失/写错/反向误配（设了名字没切作用域）、业务键实参为 null，
        // 都是编码或上游数据错误：当场抛 ArgumentException 优于静默按错误口径分桶
        await Should.ThrowAsync<ArgumentException>(() => _testService.MissingArgumentNameAsync(RandomArg()));
        await Should.ThrowAsync<ArgumentException>(() => _testService.WrongArgumentNameAsync(RandomArg()));
        await Should.ThrowAsync<ArgumentException>(() => _testService.MisconfiguredScopeAsync(RandomArg()));
        await Should.ThrowAsync<ArgumentException>(() => _testService.ByOrderNoAsync(null!, "note"));
    }
}

/* 防重复提交定向夹具（与 OperationRateLimitingInterceptorTests 的 RateLimitingTestAppService 同款组织方式）。
 * 业务失败用固定错误码的 BusinessException 模拟，测试不渲染本地化文案，无需语言资源。
 */
public class PreventDuplicateSubmitTestService : ApplicationService
{
    private int _failCalls;

    [PreventDuplicateSubmit(2)]
    public virtual Task OkAsync(string arg)
    {
        return Task.CompletedTask;
    }

    /// <summary>首次调用抛业务异常，之后放行——用于验证异常释放后可立即重试。</summary>
    [PreventDuplicateSubmit(2)]
    public virtual Task FailOnceAsync(string arg)
    {
        _failCalls++;
        if (_failCalls == 1)
        {
            throw new BusinessException("AbpAdmin:Test:SimulatedFailure");
        }

        return Task.CompletedTask;
    }

    /// <summary>始终失败且不释放窗口——对应 RuoYi "进入即标记"语义。</summary>
    [PreventDuplicateSubmit(2, DeleteKeyOnException = false)]
    public virtual Task FailAlwaysKeepsWindowAsync(string arg)
    {
        throw new BusinessException("AbpAdmin:Test:SimulatedFailure");
    }

    [PreventDuplicateSubmit(2, Scope = PreventDuplicateSubmitScope.Argument, ArgumentName = "orderNo")]
    public virtual Task ByOrderNoAsync(string orderNo, string note)
    {
        return Task.CompletedTask;
    }

    [PreventDuplicateSubmit(2, Scope = PreventDuplicateSubmitScope.Global)]
    public virtual Task GlobalOkAsync(string arg)
    {
        return Task.CompletedTask;
    }

    [PreventDuplicateSubmit(2, Scope = PreventDuplicateSubmitScope.User)]
    public virtual Task UserOkAsync(string arg)
    {
        return Task.CompletedTask;
    }

    [PreventDuplicateSubmit(2, Scope = PreventDuplicateSubmitScope.Argument)]
    public virtual Task MissingArgumentNameAsync(string arg)
    {
        return Task.CompletedTask;
    }

    [PreventDuplicateSubmit(2, Scope = PreventDuplicateSubmitScope.Argument, ArgumentName = "notExist")]
    public virtual Task WrongArgumentNameAsync(string arg)
    {
        return Task.CompletedTask;
    }

    /// <summary>反向误配：设了 ArgumentName 但没有切 Scope=Argument。</summary>
    [PreventDuplicateSubmit(2, ArgumentName = "arg")]
    public virtual Task MisconfiguredScopeAsync(string arg)
    {
        return Task.CompletedTask;
    }
}
