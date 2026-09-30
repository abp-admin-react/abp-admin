using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Volo.Abp.Caching;
using Xunit;

namespace AbpAdmin.Monitoring;

/* SCAN 分页协议的钉住测试（GetKeysAsync 的进程内协议——此前零覆盖，而页面端测试注释
 * 声称"由后端测试钉住"，是个虚假声明）。协议要件与出处：
 * - 无条件首扫（SCAN 语义里游标 0 是"从头开始"而非"已结束"，用 while 初值 0 会一次都不
 *   执行、首页恒空——round3 lens 发现的回归）；
 * - 溢出键 + 合成负游标缓冲（单次 SCAN 是"扫描提示"，可能过量投递；旧实现 .Take(N) 截断
 *   后游标已前进，溢出键跨页永久丢失——round1 审查问题）；
 * - 死页签（负游标缓冲未命中）空页终止；真实游标未命中（多实例无粘性）从该游标重扫；
 * - 稀疏 MATCH 迭代熔断（200 次上限，停在真实游标——客户端续扫不重不漏）；
 * - IsAllowedKey 后置过滤（glob 锚定被元字符破坏时的兜底，回显永不越出 ABP 键空间）。
 *
 * 驱动方式：重写 ScanPageAsync（脚本化应答）与 HydrateKeysAsync（元数据短路）两条
 * protected virtual 测试缝，GetDatabaseAsync 返回 null——分页协议是纯状态机，
 * 不需要真 Redis；真实命令交互属集成层。
 */
public class CacheMonitorScanPaginationTests
{
    /// <summary>
    /// 脚本化 SCAN 应答的服务替身：按队列顺序回放 (游标, 键集)，队列空后回放 fallback
    /// （稀疏熔断测试用它造"永不归零"的游标）。记录收到的游标/模式供断言协议行为。
    /// 可见性是刻意为之：继承 ApplicationService 即被 ABP 常规注册扫进容器，Autofac 的
    /// Castle 类代理要求类型 public、非密封、且有无参构造可解析——私有/密封会让**整个**
    /// 测试容器构建失败（殃及同容器全部测试），故必须 public class + 无参构造兜底。
    /// </summary>
    public class ScriptedScanAppService : CacheMonitorAppService
    {
        private readonly Queue<(long Cursor, string[] Keys)> _replies;
        private readonly (long Cursor, string[] Keys) _fallback;

        public List<long> ReceivedCursors { get; } = new();
        public List<string> ReceivedPatterns { get; } = new();

        /// <summary>容器构建兜底：无参构造（空脚本）让常规注册的代理生成可通过；测试都用带参构造。</summary>
        public ScriptedScanAppService()
            : this(Array.Empty<(long, string[])>())
        {
        }

        public ScriptedScanAppService(
            (long Cursor, string[] Keys)[] replies,
            (long Cursor, string[] Keys)? fallback = null,
            string? cacheKeyPrefix = null)
            : base(
                new CacheMonitorRedisConnection(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Redis:IsEnabled"] = "true",
                        // 连接串只为让 IsEnabled 判真；GetDatabaseAsync 被重写为返回 null，永不拨号
                        ["Redis:Configuration"] = "not-dialed.invalid:1",
                    }).Build()),
                Options.Create(new AbpDistributedCacheOptions { KeyPrefix = cacheKeyPrefix ?? string.Empty }))
        {
            _replies = new Queue<(long, string[])>(replies);
            _fallback = fallback ?? (0, Array.Empty<string>());
        }

        /// <summary>暴露 IsAllowedKey 供隔离边界断言（守卫的判定口径与键形依据见其注释）。</summary>
        public bool CheckAllowed(string key) => IsAllowedKey(key);

        protected override Task<IDatabase> GetDatabaseAsync() => Task.FromResult<IDatabase>(null!);

        protected override Task<(long Cursor, List<string> Keys)> ScanPageAsync(
            IDatabaseAsync database, long cursor, string pattern, int countHint)
        {
            ReceivedCursors.Add(cursor);
            ReceivedPatterns.Add(pattern);
            var reply = _replies.Count > 0 ? _replies.Dequeue() : _fallback;
            return Task.FromResult((reply.Cursor, reply.Keys.ToList()));
        }

        protected override Task<List<CacheKeyDto>> HydrateKeysAsync(IDatabase database, List<string> page)
            => Task.FromResult(page
                .Select(k => new CacheKeyDto { Key = k, Type = "string" })
                .ToList());
    }

    [Fact]
    public async Task First_Page_Always_Scans_At_Least_Once_Even_When_Server_Returns_Empty()
    {
        // 首页（cursor=0）必须无条件 SCAN：用 while 且初值 0 的写法会一次都不扫、首页恒空
        var service = new ScriptedScanAppService([(0, Array.Empty<string>())]);

        var result = await service.GetKeysAsync(null, 0, 50);

        service.ReceivedCursors.ShouldBe(new[] { 0L });
        result.Keys.ShouldBeEmpty();
        result.NextCursor.ShouldBe(0L);
    }

    [Fact]
    public async Task Overflow_Keys_Are_Buffered_And_Resumed_Via_Synthetic_Negative_Cursor()
    {
        // SCAN 过量投递（页大小 2 却回了 3 键）：溢出键 + 真实游标进缓冲，发合成负游标
        var service = new ScriptedScanAppService(
        [
            (17, new[] { "c:1", "c:2", "c:3" }),
            (0, new[] { "c:4" }),
        ]);

        var first = await service.GetKeysAsync(null, 0, 2);

        first.Keys.Select(k => k.Key).ShouldBe(new[] { "c:1", "c:2" });
        // 合成游标必为负（SCAN 真实游标恒非负，永不相撞）
        first.NextCursor.ShouldBeLessThan(0);

        // 续页：溢出键先入页，再从缓冲携带的真实游标 17 继续 SCAN
        var second = await service.GetKeysAsync(null, first.NextCursor, 2);

        second.Keys.Select(k => k.Key).ShouldBe(new[] { "c:3", "c:4" });
        second.NextCursor.ShouldBe(0L); // 无溢出：真实游标即全部状态
        service.ReceivedCursors.ShouldBe(new[] { 0L, 17L });
    }

    [Fact]
    public async Task Unknown_Synthetic_Cursor_Returns_Empty_Page_Without_Scanning()
    {
        // 死页签（中途关页后继续翻/进程重启）：负游标命中不了缓冲，从哪续已不可知——
        // 如实空页终止，绝不拿负数游标打 Redis
        var service = new ScriptedScanAppService([(0, Array.Empty<string>())]);

        var result = await service.GetKeysAsync(null, -4242, 50);

        result.Keys.ShouldBeEmpty();
        result.NextCursor.ShouldBe(0L);
        service.ReceivedCursors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_Real_Cursor_Restarts_Scan_From_That_Cursor()
    {
        // 多实例无粘性：续页落到别的实例、缓冲未命中，但游标是真实的——从该游标重扫
        // （溢出键不可恢复，游标语义保证之后的键不重不漏）
        var service = new ScriptedScanAppService([(0, new[] { "c:x" })]);

        var result = await service.GetKeysAsync(null, 42, 50);

        result.Keys.Select(k => k.Key).ShouldBe(new[] { "c:x" });
        result.NextCursor.ShouldBe(0L);
        service.ReceivedCursors.ShouldBe(new[] { 42L });
    }

    [Fact]
    public async Task Sparse_Match_Fuse_Stops_At_Bounded_Iterations_And_Returns_Real_Cursor()
    {
        // 稀疏 MATCH：每轮都凑不满一页且游标永不归零——熔断防住全键空间死循环。
        // 无条件首扫 1 次 + while 迭代上限 200 次 = 恰 201 次往返
        var service = new ScriptedScanAppService(
            Array.Empty<(long, string[])>(),
            fallback: (99, Array.Empty<string>()));

        var result = await service.GetKeysAsync(null, 0, 200);

        service.ReceivedCursors.Count.ShouldBe(201);
        service.ReceivedCursors.ShouldAllBe(c => c == 0 || c == 99);
        result.Keys.ShouldBeEmpty();
        result.NextCursor.ShouldBe(99L); // 停在真实游标，客户端续扫不重不漏
    }

    [Fact]
    public async Task Scan_Results_Are_PostFiltered_To_The_Abp_Keyspace()
    {
        // glob 锚定段理论上可被用户词中的元字符破坏——IsAllowedKey 后置过滤兜底：
        // 其它系统的键（含本框架的 DataProtection 密钥环）绝不入页
        var service = new ScriptedScanAppService(
        [
            (0, new[] { "c:ok-1", "other-system:key", "t:ok-2", "AbpAdmin:DataProtection-Keys:Production" }),
        ]);

        var result = await service.GetKeysAsync(null, 0, 50);

        result.Keys.Select(k => k.Key).ShouldBe(new[] { "c:ok-1", "t:ok-2" });
        result.NextCursor.ShouldBe(0L);
    }

    [Fact]
    public async Task Configured_Isolation_Prefix_Tightens_The_Boundary_To_Own_Keys_Only()
    {
        // 配置了隔离前缀的部署：其它 ABP 应用（不同前缀/无前缀）的键必须被后置过滤掉——
        // 隔离是硬边界不是页面过滤词（键形依据见 DistributedCacheKeyShapeTests）
        var tenantKey = "t:3a23d8e4-1122-3344-5566-778899aabbcc,c:MyCache,k:App1:k4";
        var service = new ScriptedScanAppService(
        [
            (0, new[] { "c:MyCache,k:App1:k1", "c:TheirCache,k:App2:k2", "c:NoPrefix:k3", tenantKey }),
        ],
        cacheKeyPrefix: "App1:");

        var result = await service.GetKeysAsync(null, 0, 50);

        result.Keys.Select(k => k.Key).ShouldBe(new[] { "c:MyCache,k:App1:k1", tenantKey });
    }

    [Fact]
    public void Configured_Isolation_Prefix_Guards_Read_And_Delete_Boundary()
    {
        // 守卫判定的表驱动钉住（读/删共用 AssertAbpKey → IsAllowedKey）：
        // 结构前缀 + 隔离前缀双条件，缺一不可
        var service = new ScriptedScanAppService(
            Array.Empty<(long, string[])>(),
            cacheKeyPrefix: "App1:");

        service.CheckAllowed("c:MyCache,k:App1:k1").ShouldBeTrue();
        service.CheckAllowed("t:3a23d8e4-1122-3344-5566-778899aabbcc,c:MyCache,k:App1:k1").ShouldBeTrue();
        service.CheckAllowed("c:TheirCache,k:App2:k2").ShouldBeFalse(); // 其它应用的键
        service.CheckAllowed("c:NoPrefix:k3").ShouldBeFalse();          // 无前缀部署的键
        service.CheckAllowed("other-system:key").ShouldBeFalse();
        service.CheckAllowed("App1:_AbpSomeHash").ShouldBeFalse();      // stamp 键不在 c:/t: 结构内（既有口径）
    }

    [Fact]
    public async Task Synthetic_Cursor_From_A_Different_Pattern_Is_Treated_As_Dead()
    {
        // 同一合成游标换过滤词再来（模式从 [ct]:** 变 [ct]:*tenant*）：缓冲条目模式不匹配，
        // 不得把别的口径下缓冲的溢出键续给本次——按死页签空页终止
        var service = new ScriptedScanAppService([(17, new[] { "c:1", "c:2", "c:3" })]);

        var first = await service.GetKeysAsync(null, 0, 2);
        first.NextCursor.ShouldBeLessThan(0);

        var second = await service.GetKeysAsync("tenant", first.NextCursor, 2);

        second.Keys.ShouldBeEmpty();
        second.NextCursor.ShouldBe(0L);
        service.ReceivedCursors.ShouldBe(new[] { 0L }); // 未再 SCAN
    }
}
