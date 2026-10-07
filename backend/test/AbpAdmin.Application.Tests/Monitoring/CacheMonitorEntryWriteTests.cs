using System;
using System.Threading.Tasks;
using AbpAdmin.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace AbpAdmin.Monitoring;

/* 写值/改期的钉住测试（对标 abp-next-admin CachingManagement 的 Set/Refresh 补齐的两个动作）。
 *
 * 覆盖面分层与 SCAN 分页测试同一原则：元数据解析/合并/TTL 合成是纯状态机（RedisCache
 * 条目格式的数学），internal static 直测；Redis 交互（HSET/HGET/EXPIRE 的真实往返）
 * 属集成层，由端到端验证。这里额外钉住两条不依赖 Redis 的守卫次序：
 * - AssertAbpKey 先于一切数据库访问（越界键在 Memory 后端、无连接的测试环境就能验证）；
 * - 隔离前缀收紧后写值/改期与删除同一硬边界（此前缀是操作边界，不只是浏览过滤词）。
 *
 * 元数据字段语义出处（Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache 实现）：
 * absexp = 绝对过期 epoch 毫秒（-1=无）；sldexp = 滑动过期 TimeSpan ticks（-1=无）；
 * 键 TTL = min(absexp 距今, sldexp)。
 */
public class CacheMonitorEntryWriteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>与服务实现共享的最小替身：重写 GetDatabaseAsync 返回 null（守卫次序验证不触库）。</summary>
    public class GuardProbeAppService : CacheMonitorAppService
    {
        public GuardProbeAppService(string? keyPrefix = null, bool redisEnabled = false)
            : base(
                new CacheMonitorRedisConnection(new ConfigurationBuilder()
                    .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                    {
                        ["Redis:IsEnabled"] = redisEnabled ? "true" : "false",
                        ["Redis:Configuration"] = "not-dialed.invalid:1",
                    }).Build()),
                Options.Create(new AbpDistributedCacheOptions { KeyPrefix = keyPrefix ?? string.Empty }))
        {
        }

        protected override Task<IDatabase> GetDatabaseAsync() => Task.FromResult<IDatabase>(null!);
    }

    // ---------- ParseMetadata ----------

    [Fact]
    public void ParseMetadata_Minus1_And_Missing_Fields_Are_Unset()
    {
        var (abs, sld) = CacheMonitorAppService.ParseMetadata("-1", RedisValue.Null);
        abs.ShouldBeNull();
        sld.ShouldBeNull();
    }

    [Fact]
    public void ParseMetadata_Valid_Fields_Are_Decoded()
    {
        var (abs, sld) = CacheMonitorAppService.ParseMetadata("1770000000000", "12000000000");
        abs.ShouldBe(1770000000000L);
        sld.ShouldBe(TimeSpan.FromMinutes(20)); // 12000000000 ticks = 20 分钟（RedisCache 默认滑动）
    }

    // ---------- MergeMetadata ----------

    [Fact]
    public void MergeMetadata_Null_Inputs_Keep_Current_Metadata()
    {
        var (abs, sld) = CacheMonitorAppService.MergeMetadata(
            1770000000000L, TimeSpan.FromMinutes(10), null, null, Now);
        abs.ShouldBe(1770000000000L);
        sld.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void MergeMetadata_Provided_Seconds_Override_And_Are_Relative_To_Now()
    {
        var (abs, sld) = CacheMonitorAppService.MergeMetadata(
            1770000000000L, TimeSpan.FromMinutes(10), 3600, 300, Now);
        abs.ShouldBe(Now.AddHours(1).ToUnixTimeMilliseconds());
        sld.ShouldBe(TimeSpan.FromMinutes(5));
    }

    // ---------- ComputeRedisTtl ----------

    [Fact]
    public void ComputeRedisTtl_Both_Present_Takes_Minimum()
    {
        var ttl = CacheMonitorAppService.ComputeRedisTtl(
            Now.AddHours(1).ToUnixTimeMilliseconds(), TimeSpan.FromMinutes(5), Now);
        ttl.ShouldBe(TimeSpan.FromMinutes(5)); // 滑动比绝对短 → 取滑动
    }

    [Fact]
    public void ComputeRedisTtl_Single_Side_Is_Used()
    {
        CacheMonitorAppService.ComputeRedisTtl(Now.AddMinutes(30).ToUnixTimeMilliseconds(), null, Now)
            .ShouldBe(TimeSpan.FromMinutes(30));
        CacheMonitorAppService.ComputeRedisTtl(null, TimeSpan.FromMinutes(30), Now)
            .ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ComputeRedisTtl_No_Metadata_Is_Persistent()
    {
        CacheMonitorAppService.ComputeRedisTtl(null, null, Now).ShouldBeNull();
    }

    [Fact]
    public void ComputeRedisTtl_Past_Absolute_Expiration_Is_Negative()
    {
        var ttl = CacheMonitorAppService.ComputeRedisTtl(
            Now.AddMinutes(-1).ToUnixTimeMilliseconds(), TimeSpan.FromMinutes(30), Now);
        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeLessThan(TimeSpan.Zero);
    }

    [Fact]
    public void AssertFutureTtl_Past_Ttl_Is_Rejected_Not_Silently_Deleted()
    {
        // 负 TTL 直接 EXPIRE 会把键当场删掉——删除必须走显式的删除功能
        Should.Throw<UserFriendlyException>(() =>
            CacheMonitorAppService.AssertFutureTtl(TimeSpan.FromSeconds(-60)));
        Should.NotThrow(() => CacheMonitorAppService.AssertFutureTtl(TimeSpan.FromMinutes(5)));
        Should.NotThrow(() => CacheMonitorAppService.AssertFutureTtl(null)); // 永不过期合法
    }

    // ---------- 守卫次序（写值/改期与删除同一硬边界，先于一切数据库访问） ----------

    [Fact]
    public async Task SetValue_Disallowed_Key_Is_Rejected_Before_Any_Database_Access()
    {
        // Memory 后端 + 无连接：能抛 KeyNotAllowed 而不是 RedisDisabled，即证明守卫在前
        var service = new GuardProbeAppService(keyPrefix: "AbpAdmin:");

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            service.SetValueAsync(new CacheSetValueInput
            {
                Key = "c:Volo.Abp.SettingManagement.Setting,k:OtherApp:x",
                Value = "{}",
            }));
        ex.Code.ShouldBe("AbpAdmin:CacheMonitorKeyNotAllowed");
    }

    [Fact]
    public async Task Refresh_Disallowed_Key_Is_Rejected_Before_Any_Database_Access()
    {
        var service = new GuardProbeAppService(keyPrefix: "AbpAdmin:");

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            service.RefreshAsync(new CacheRefreshInput { Key = "t:tenant-id,c:OtherApp,k:x" }));
        ex.Code.ShouldBe("AbpAdmin:CacheMonitorKeyNotAllowed");
    }

    [Fact]
    public async Task SetValue_Allowed_Key_Reaches_Backend_Gate()
    {
        // 守卫放行后的下一道闸是 RequireDatabaseAsync（Memory 后端 → RedisDisabled）：
        // 证明守卫链次序正确（隔离边界 → 后端闸 → Redis 交互）
        var service = new GuardProbeAppService(keyPrefix: "AbpAdmin:", redisEnabled: false);

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            service.SetValueAsync(new CacheSetValueInput
            {
                Key = "c:Volo.Abp.SettingManagement.Setting,k:AbpAdmin:x",
                Value = "{}",
            }));
        ex.Code.ShouldBe("AbpAdmin:CacheMonitorRedisDisabled");
    }
}
