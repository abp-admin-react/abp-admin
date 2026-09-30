using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Caching;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.Caching;

/// <summary>
/// 缓存键统一前缀（AbpDistributedCacheOptions.KeyPrefix）的配置接线：共享 Redis 上
/// 多应用隔离的官方机制（详见 CachingAndTenancyConfigurator 注释），本类钉住
/// 「配置了就用、没配保持空（结构性前缀 c:/t:）」两条分支——缓存监控的键空间锚定
/// （AssertAbpKey/BuildScanPattern）按同一 KeyPrefix 判定，接线断了监控会静默退回
/// c:/t: 口径，必须红测试兜底。
/// </summary>
public class CacheKeyPrefixConfigurationTests
{
    [Fact]
    public async Task Configured_Prefix_Is_Applied_To_All_Distributed_Cache_Keys()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("DistributedCache:KeyPrefix", "TestApp:"),
        ]);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<AbpDistributedCacheOptions>>()
            .Value.KeyPrefix.ShouldBe("TestApp:");
    }

    [Fact]
    public async Task Absent_Prefix_Keeps_The_Default_Empty_Structural_Prefix()
    {
        // 出厂形态契约（HostUnderTest 刻意不加载 appsettings.secrets.json，见其注释）：
        // 「未配置/空值」分支显式覆盖空串钉住——空串走 IsNullOrWhiteSpace 不赋值（判别键
        // 兜底也拿不到），保持结构性前缀 c:/t:——独占实例/专属库的默认形态
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("DistributedCache:KeyPrefix", ""),
        ]);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<AbpDistributedCacheOptions>>()
            .Value.KeyPrefix.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Instance_Discriminator_Is_The_Default_When_KeyPrefix_Not_Configured()
    {
        // 部署身份三接线之一（缓存 KeyPrefix 默认值；另两处：DP 密钥环键名、ChannelPrefix）：
        // 只配 App:InstanceDiscriminator 也能完成缓存隔离——部署身份一处声明、三处生效
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("App:InstanceDiscriminator", "shop-a"),
        ]);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<AbpDistributedCacheOptions>>()
            .Value.KeyPrefix.ShouldBe("shop-a");
    }

    [Fact]
    public async Task Explicit_KeyPrefix_Wins_Over_The_Instance_Discriminator()
    {
        // 显式 DistributedCache:KeyPrefix 优先：缓存隔离与部署身份允许不同源（例如两套
        // 部署有意共享密钥环但隔离缓存键的场景）
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("DistributedCache:KeyPrefix", "Explicit:"),
            new KeyValuePair<string, string?>("App:InstanceDiscriminator", "shop-a"),
        ]);
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<AbpDistributedCacheOptions>>()
            .Value.KeyPrefix.ShouldBe("Explicit:");
    }
}
