using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using AbpAdmin.Posts;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.OperationLogs;

/* 解析函数契约测试：ID→名称 翻译、未找到兜底、集合入参、非 ID 值原样渲染。
 * ruoyi 对标：mzt-biz-log 的 AdminUserParseFunction 等解析函数族。
 */
public abstract class OperationLogParseFunctionTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IGuidGenerator _guidGenerator;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IRepository<Post, Guid> _postRepository;
    private readonly UserOperationLogParseFunction _userFunction;
    private readonly PostOperationLogParseFunction _postFunction;

    protected OperationLogParseFunctionTests()
    {
        _guidGenerator = GetRequiredService<IGuidGenerator>();
        _userRepository = GetRequiredService<IIdentityUserRepository>();
        _postRepository = GetRequiredService<IRepository<Post, Guid>>();
        _userFunction = GetRequiredService<UserOperationLogParseFunction>();
        _postFunction = GetRequiredService<PostOperationLogParseFunction>();
    }

    [Fact]
    public async Task Should_Resolve_User_And_Post_Names()
    {
        var userId = _guidGenerator.Create();
        var postId = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await _userRepository.InsertAsync(new IdentityUser(userId, "parse-fn-user", "parse-fn-user@test.local"));
            await _postRepository.InsertAsync(new Post(postId, "解析函数测试岗位", "PF001"));
        });

        (await _userFunction.ResolveAsync(userId)).ShouldBe("parse-fn-user");
        (await _postFunction.ResolveAsync(postId)).ShouldBe("解析函数测试岗位");
    }

    [Fact]
    public async Task Unknown_Id_Falls_Back_To_Marker()
    {
        var missing = _guidGenerator.Create();
        var resolved = await _userFunction.ResolveAsync(missing);

        resolved.ShouldNotBeNull();
        resolved.ShouldStartWith("未知(");
        resolved.ShouldContain(missing.ToString()[..8]);
    }

    [Fact]
    public async Task Collection_Input_Resolves_Each_Item()
    {
        var user1 = _guidGenerator.Create();
        var user2 = _guidGenerator.Create();
        var missing = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await _userRepository.InsertAsync(new IdentityUser(user1, "parse-fn-a", "parse-fn-a@test.local"));
            await _userRepository.InsertAsync(new IdentityUser(user2, "parse-fn-b", "parse-fn-b@test.local"));
        });

        // Guid 字符串（如 OpenIddict subject）与原生 Guid 混合
        var resolved = await _userFunction.ResolveAsync(new List<object>
        {
            user1,
            user2.ToString(),
            missing,
        });

        resolved.ShouldBe($"parse-fn-a, parse-fn-b, 未知({missing.ToString()[..8]})");
    }

    [Fact]
    public async Task Non_Id_Value_Renders_As_Is_Instead_Of_Dropping()
    {
        // 模板把普通字符串喂给函数时不能静默变空：原样渲染保留信息
        (await _userFunction.ResolveAsync("admin-not-a-guid")).ShouldBe("admin-not-a-guid");
        (await _userFunction.ResolveAsync(null)).ShouldBeNull();
    }

    [Fact]
    public async Task Cache_Hit_Should_Return_Stale_Name_Within_Ttl()
    {
        // 缓存契约：实体改名后，TTL 窗口内解析结果允许滞后（换取不打穿数据库）
        var userId = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() =>
            _userRepository.InsertAsync(new IdentityUser(userId, "parse-fn-old", "parse-fn-old@test.local")));

        (await _userFunction.ResolveAsync(userId)).ShouldBe("parse-fn-old");

        var userManager = GetRequiredService<IdentityUserManager>();
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await userManager.GetByIdAsync(userId);
            (await userManager.SetUserNameAsync(user, "parse-fn-new")).Succeeded.ShouldBeTrue();
        });

        (await _userFunction.ResolveAsync(userId)).ShouldBe("parse-fn-old");
    }

    [Fact]
    public async Task Unknown_Fallback_Should_Be_Stable_Across_Calls()
    {
        // 负结果缓存：第二次解析同一未知 Id 不再打库，结果与第一次一致
        var missing = _guidGenerator.Create();
        var first = await _userFunction.ResolveAsync(missing);
        var second = await _userFunction.ResolveAsync(missing);
        first.ShouldBe(second);
        first.ShouldStartWith("未知(");
    }

    [Theory]
    [InlineData(20, false)] // 恰好 20 项：不误加省略号（off-by-one 回归）
    [InlineData(21, true)]  // 21 项：第 21 项被截断，加省略号
    public async Task Collection_Cap_Should_Append_Ellipsis_Only_When_Truncated(int count, bool expectEllipsis)
    {
        var ids = Enumerable.Range(0, count)
            .Select(_ => _guidGenerator.Create())
            .Cast<object>()
            .ToList();

        var resolved = await _userFunction.ResolveAsync(ids);

        resolved.ShouldNotBeNull();
        resolved.EndsWith("…").ShouldBe(expectEllipsis);
        // 未被截断时：全部 20 项都在；截断时：前 20 项 + 省略号
        resolved.Count(x => x == ',').ShouldBe(expectEllipsis ? 20 : 19);
    }

    /// <summary>基类防线：FindNameAsync 抛异常时 ResolveAsync 返回 null（引擎侧另有第二道防线）。</summary>
    private sealed class ThrowingParseFunction : OperationLogParseFunctionBase
    {
        public override string Name => "throwing";

        public ThrowingParseFunction(Volo.Abp.Caching.IDistributedCache<OperationLogNameCacheItem> cache)
            : base(cache)
        {
        }

        protected override ValueTask<string?> FindNameAsync(Guid id)
            => throw new InvalidOperationException("仓储故障");
    }

    [Fact]
    public async Task Base_Class_Should_Swallow_FindName_Exceptions()
    {
        // 手工构造（私有测试替身不进 DI）：缓存取测试基座真实实现
        var function = new ThrowingParseFunction(
            GetRequiredService<Volo.Abp.Caching.IDistributedCache<OperationLogNameCacheItem>>());
        (await function.ResolveAsync(Guid.NewGuid())).ShouldBeNull();
    }
}
