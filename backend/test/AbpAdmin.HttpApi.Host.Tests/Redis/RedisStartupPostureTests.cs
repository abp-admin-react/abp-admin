using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Medallion.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.Redis;

/// <summary>
/// Redis 启动姿态的钉住测试：姿态告警纯判定（<see cref="AbpAdminHttpApiHostModule"/> 的
/// ShouldWarnOn* 两个 internal 纯函数）、共享连接的首用 fail-fast 与失败冷却、以及
/// appsettings.Production.json 多实例模板不被悄悄改回去。
/// </summary>
public class RedisStartupPostureTests
{
    private static IConfiguration Config(params KeyValuePair<string, string?>[] pairs)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(pairs)
            .Build();
    }

    [Theory]
    [InlineData("true", "false", true)]   // backplane 声明多实例而 Redis 显式关——要告警
    [InlineData("true", null, false)]     // 缺省 = 启用（与 ConfigureRedis 同口径），姿态自洽
    [InlineData("true", "true", false)]
    [InlineData("false", "false", false)] // 未宣告多实例，怎么配都不属于本告警
    [InlineData(null, "false", false)]
    public void Incoherent_Posture_Detection_Follows_The_Documented_Key_Semantics(
        string? backplane, string? redisEnabled, bool expected)
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        if (backplane != null) pairs.Add(new("SignalR:UseRedisBackplane", backplane));
        if (redisEnabled != null) pairs.Add(new("Redis:IsEnabled", redisEnabled));

        AbpAdminHttpApiHostModule.ShouldWarnOnIncoherentRedisPosture(Config([.. pairs]))
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("Production", "127.0.0.1:6379", true)]   // 生产 + 出厂默认连接串——多半忘了覆盖
    [InlineData("Production", "redis.internal:6379", false)]
    [InlineData("Development", "127.0.0.1:6379", false)] // 本机调试 Redis 在默认端口是常态，豁免
    [InlineData("Production", null, false)]               // 未启用 Redis 不归本告警管
    [InlineData("Staging", "127.0.0.1:6379", true)]
    public void Template_Default_Configuration_Warning(
        string environment, string? redisConfiguration, bool expected)
    {
        var pairs = new List<KeyValuePair<string, string?>>
        {
            new("Redis:IsEnabled", "true"),
        };
        if (redisConfiguration != null) pairs.Add(new("Redis:Configuration", redisConfiguration));

        AbpAdminHttpApiHostModule.ShouldWarnOnTemplateRedisConfiguration(Config([.. pairs]), environment)
            .ShouldBe(expected);
    }

    /// <summary>
    /// 首用 fail-fast：解析 <c>IDistributedLockProvider</c> 即触发共享连接的首次 Connect
    /// （工厂内 GetDatabase）——端口指向回环无监听地址，立即被拒绝而不是挂起。锁消费方
    /// （防重复提交/限流/退款）全部按基础设施错误向上传播（fail-closed），本测试钉住
    /// 「显式抛错、绝不静默退化为进程内锁」这一姿态；同时验证失败进入冷却期：冷却期内的
    /// 再次解析快速抛出冷却异常，不再重复拨号。
    /// </summary>
    [Fact]
    public async Task First_Use_Fails_Fast_Explicitly_And_Enters_Cooldown_Instead_Of_Caching_Forever()
    {
        // 有意在本用例里真的拨号（与同程序集其它测试的「建图零拨号」纪律互补：那些测试
        // 保证不拨，本测试保证拨了会怎样）。127.0.0.1:1 特权端口无监听，连接被立即拒绝。
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("Redis:IsEnabled", "true"),
            new KeyValuePair<string, string?>("Redis:Configuration", "127.0.0.1:1"),
        ]);
        await using var provider = services.BuildServiceProvider();

        Should.Throw<Exception>(() =>
            provider.GetRequiredService<IDistributedLockProvider>());

        // 冷却期内：不再重拨（快速失败），异常信息带冷却标记——这是「失败不永久缓存」的
        // 前半段；后半段（冷却结束后可重试成功）需要真 Redis，属集成测试层
        var second = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<IDistributedLockProvider>());
        second.Message.ShouldContain("cooldown");
    }

    /// <summary>
    /// 生产模板的三件多实例强制项（backplane / Redis:IsEnabled / Quartz 持久化）不许静默
    /// 改回去——改掉任何一项，多实例部署就会出现跨实例推送丢失或令牌互验失败，而没有任何
    /// 测试变红。直接读宿主源文件（HostUnderTest 钉 Development 环境，加载不到该文件）。
    /// </summary>
    [Fact]
    public void Production_Template_Keeps_The_Multi_Instance_Posture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AbpAdmin.slnx")))
        {
            directory = directory.Parent;
        }
        directory.ShouldNotBeNull("测试程序集应位于仓库内，向上能找到 AbpAdmin.slnx");

        var productionJson = Path.Combine(
            directory.FullName, "src", "AbpAdmin.HttpApi.Host", "appsettings.Production.json");
        File.Exists(productionJson).ShouldBeTrue();

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(productionJson, optional: false)
            .Build();

        configuration.GetValue<bool>("SignalR:UseRedisBackplane").ShouldBeTrue();
        configuration.GetValue<bool>("Redis:IsEnabled").ShouldBeTrue();
        configuration.GetValue<bool>("Quartz:UsePersistentStore").ShouldBeTrue();
    }
}
