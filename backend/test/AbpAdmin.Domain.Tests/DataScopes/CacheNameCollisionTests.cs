using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.DataScopes;

/// <summary>
/// T1.5.0 [CacheName] 撞键一次性验证。
///
/// 验证：两个字段形状不同的缓存项类型打同一个 [CacheName]，
/// 用同一个业务键写入再读取，是否真的会读到对方的字节并抛反序列化异常。
///
/// 结论决定「模拟一次 schema 变更的滚动发布」那条验收项的执行环境分档：
/// - 撞得上 → 开发环境也能用「同名 [CacheName] 的旧形状类型写入旧键」造出
///   「旧字节被新类型读到」，对照组可以在本机复现
/// - 撞不上 → 那条只能整条留在真 Redis 环境
///
/// 本测试在开发环境（MemoryDistributedCache）就能跑，不需要真 Redis。
/// </summary>
public abstract class CacheNameCollisionTests<TStartupModule> : AbpAdminDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IDistributedCache<CacheItemShapeA> _cacheA;
    private readonly IDistributedCache<CacheItemShapeB> _cacheB;

    protected CacheNameCollisionTests()
    {
        _cacheA = GetRequiredService<IDistributedCache<CacheItemShapeA>>();
        _cacheB = GetRequiredService<IDistributedCache<CacheItemShapeB>>();
    }

    [Fact]
    public async Task Same_CacheName_Same_Key_Different_Shape_Should_Throw_On_Deserialize()
    {
        const string key = "collision-test-key";

        // 用类型 A 写入
        await _cacheA.SetAsync(key, new CacheItemShapeA
        {
            Name = "hello",
            Count = 42
        });

        // 用类型 B 读取同一个键——两者 [CacheName] 相同、业务键相同，
        // 但字段形状不同（A 有 Name/Count，B 有 Title/Amount）
        // 如果键空间不隔离，B 会读到 A 写入的字节，反序列化时抛异常
        var exception = await Record.ExceptionAsync(async () =>
        {
            await _cacheB.GetAsync(key);
        });

        // 撞得上：读到了对方的字节，反序列化失败
        // System.Text.Json 对未知字段默认忽略，但如果字段名碰巧匹配但类型不匹配，
        // 或者 JSON 结构不兼容，会抛 JsonException 或 InvalidOperationException
        //
        // 注意：System.Text.Json 对「字段名不匹配」是宽容的（忽略未知字段），
        // 所以 ShapeA 的 {"Name":"hello","Count":42} 被 ShapeB 读取时，
        // Name→不存在于B（忽略），Count→不存在于B（忽略），
        // Title→不存在于JSON（默认null），Amount→不存在于JSON（默认0）。
        // 这种情况下不会抛异常，而是返回一个「全默认值」的 B 实例。
        //
        // 真正会抛异常的场景是：字段名碰巧相同但类型不同。
        // 所以我们用 ShapeC 来验证——它与 ShapeA 有同名字段但类型不同。
        //
        // 先记录 B 的读取结果（可能不抛异常，返回默认值）
        if (exception == null)
        {
            // 没抛异常，说明 System.Text.Json 忽略了不匹配字段，返回了默认值
            // 这也是「撞键」的证据——B 读到了 A 的字节，只是反序列化没失败
            var itemB = await _cacheB.GetAsync(key);
            itemB.ShouldNotBeNull();
            // B 的字段应该都是默认值（因为 JSON 里没有 B 的字段）
            itemB.Title.ShouldBeNull();
            itemB.Amount.ShouldBe(0);
        }

        // 用 ShapeC（与 ShapeA 有同名字段但类型不同）验证真正的反序列化异常
        var cacheC = GetRequiredService<IDistributedCache<CacheItemShapeC>>();
        var exceptionC = await Record.ExceptionAsync(async () =>
        {
            await cacheC.GetAsync(key);
        });

        // ShapeC.Count 是 string 类型，而 JSON 里 Count 是数字 42
        // System.Text.Json 会抛 JsonException（或 InvalidOperationException 包装）
        exceptionC.ShouldNotBeNull(
            "两个类型打同一个 [CacheName] 时，用同一个业务键读取应当读到对方的字节。" +
            "如果这里没抛异常，说明键空间在某一层被隔离了，需要重新评估「模拟 schema 变更」验收项的分档。");
    }
}

/// <summary>
/// 缓存项形状 A：Name(string) + Count(int)
/// </summary>
[CacheName("CollisionTest")]
public class CacheItemShapeA
{
    public string? Name { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// 缓存项形状 B：Title(string) + Amount(decimal)
/// 与 ShapeA 字段名完全不同——验证「读到对方字节但字段不匹配」的场景
/// </summary>
[CacheName("CollisionTest")]
public class CacheItemShapeB
{
    public string? Title { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// 缓存项形状 C：Name(string) + Count(string)
/// 与 ShapeA 有同名字段 Count 但类型不同（int vs string）——验证真正的反序列化异常
/// </summary>
[CacheName("CollisionTest")]
public class CacheItemShapeC
{
    public string? Name { get; set; }
    public string? Count { get; set; }
}
