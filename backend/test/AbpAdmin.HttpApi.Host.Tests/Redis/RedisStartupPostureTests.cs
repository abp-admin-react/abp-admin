using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Medallion.Threading;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    [InlineData("true", "false", "redis:6379", true)]   // backplane 声明多实例而 Redis 显式关——要告警
    [InlineData("true", null, "redis:6379", false)]     // 缺省 = 启用（与 ConfigureRedis 同口径），姿态自洽
    [InlineData("true", "true", "redis:6379", false)]
    [InlineData("false", "false", "redis:6379", false)] // 未宣告多实例，怎么配都不属于本告警
    [InlineData(null, "false", "redis:6379", false)]
    // Redis:Configuration 为空：ConfigureSignalRBackplane 直接退出、backplane 已退化为进程内
    // 广播，"跨实例广播中而锁留在本地"的诊断不成立——不告警（此前会误诊，见谓词前置条件注释）
    [InlineData("true", "false", null, false)]
    [InlineData("true", "false", "  ", false)]
    public void Incoherent_Posture_Detection_Follows_The_Documented_Key_Semantics(
        string? backplane, string? redisEnabled, string? redisConfiguration, bool expected)
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        if (backplane != null) pairs.Add(new("SignalR:UseRedisBackplane", backplane));
        if (redisEnabled != null) pairs.Add(new("Redis:IsEnabled", redisEnabled));
        if (redisConfiguration != null) pairs.Add(new("Redis:Configuration", redisConfiguration));

        AbpAdminHttpApiHostModule.ShouldWarnOnIncoherentRedisPosture(Config([.. pairs]))
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("Production", "true", "127.0.0.1:6379", true)]   // 生产 + 出厂默认连接串——多半忘了覆盖
    [InlineData("Production", "true", "redis.internal:6379", false)]
    [InlineData("Development", "true", "127.0.0.1:6379", false)] // 本机调试 Redis 在默认端口是常态，豁免
    [InlineData("Production", "true", null, false)]               // 没配连接串不归本告警管
    [InlineData("Staging", "true", "127.0.0.1:6379", true)]
    // 键位口径与 ConfigureRedis 同口径（缺省/空 = 启用）：IsEnabled 没写也要按启用告警。
    // 此前实现把 TryParse 失败当"未启用"静默放过——缺省启用 + 模板串恰是本告警要抓的姿态
    [InlineData("Production", null, "127.0.0.1:6379", true)]
    [InlineData("Production", "", "127.0.0.1:6379", true)]
    [InlineData("Production", "false", "127.0.0.1:6379", false)]  // 显式关：Redis 子系统未启用
    public void Template_Default_Configuration_Warning(
        string environment, string? redisEnabled, string? redisConfiguration, bool expected)
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        if (redisEnabled != null) pairs.Add(new("Redis:IsEnabled", redisEnabled));
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
        // 前半段；后半段（冷却结束后可重试成功）需要真 Redis，属集成测试层。
        // 原始异常必须挂在 InnerException 上：消费方要能继续区分连接类/配置类故障，
        // 冷却包装不得吞掉类型（审计发现此前的包装是裸 InvalidOperationException）
        var second = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<IDistributedLockProvider>());
        second.Message.ShouldContain("cooldown");
        second.InnerException.ShouldNotBeNull();
    }

    /// <summary>
    /// 关停闩锁：Dispose 之后 GetDatabase 必须抛 <see cref="ObjectDisposedException"/>，
    /// 而不是复活一条永不释放的新连接——关停窗口里 DP 密钥环（RedisXmlRepository 持有
    /// 惰性委托）仍可能调工厂。经由密钥环仓储直接持有委托调用，绕开已释放容器的解析
    /// 检查，命中连接自身的闩锁。
    /// </summary>
    [Fact]
    public async Task Disposed_Connection_Refuses_Further_Use_Instead_Of_Resurrecting()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("Redis:IsEnabled", "true"),
            new KeyValuePair<string, string?>("Redis:Configuration", "127.0.0.1:1"),
        ]);
        var provider = services.BuildServiceProvider();

        // 建图零拨号契约下仓储只是存下委托，不连 Redis（见 DataProtectionServiceRegistrationTests）
        var repository = provider.GetRequiredService<IOptions<KeyManagementOptions>>()
            .Value.XmlRepository;
        repository.ShouldNotBeNull();

        // SharedRedisConnection 是宿主私有嵌套类型，按名反射解析一次：MS DI 对常量单例
        // 只有解析过才接管释放（生产路径由启动探针解析锁提供程序时顺带接管），随后
        // DisposeAsync 才会真的释放连接、落下闩锁
        var connectionType = typeof(AbpAdminHttpApiHostModule).Assembly
            .GetTypes().First(t => t.Name == "SharedRedisConnection");
        var connection = provider.GetRequiredService(connectionType);
        await provider.DisposeAsync();

        // 容器释放路径以反射直达 Dispose 收尾：生产宿主走 Autofac（RegisterInstance 的释放
        // 语义与 MS DI 的按解析捕获不同），本测试钉的是连接自身的关停闩锁语义，不是某个
        // 容器的释放策略——MS DI 手工 Build 的 provider 实测不释放未解析工厂的常量单例
        connectionType.GetMethod("Dispose")!.Invoke(connection, null); // 闩锁落下

        // 没有闩锁的旧行为：_multiplexer 为空 + 无冷却记录 → 重新 Connect（哪怕连不上也要
        // 付一次拨号；连得上就是泄漏）。闩锁后必须显式 ObjectDisposedException
        Should.Throw<ObjectDisposedException>(() => repository!.GetAllElements());
    }

    /// <summary>
    /// 非法 Redis:IsEnabled（如 "yes"）必须让宿主启动失败，而不是静默当作关闭——
    /// 多实例部署里那等于把分布式锁悄悄关掉。此前该 fail-fast 只有注释宣称（模块、
    /// HostServiceGraphValidationTests、HostInitializationLogTests 三处），无测试钉住。
    /// </summary>
    [Fact]
    public async Task Invalid_RedisIsEnabled_Fails_Host_Startup_Instead_Of_Silently_Disabling_Locks()
    {
        await Should.ThrowAsync<Exception>(() => HostUnderTest.CreateBuilderAsync(
        [
            new KeyValuePair<string, string?>("Redis:IsEnabled", "yes"),
            new KeyValuePair<string, string?>("Redis:Configuration", "127.0.0.1:1"),
        ]));
    }

    /// <summary>
    /// 钉住 AbortOnConnectFail 的强制覆盖：abortConnect=false 是连接串常见默认（Azure 风格），
    /// 不覆盖时 Connect 会静默返回未连接的多路复用器——锁/DP 拿到的 IDatabase 带病运行，
    /// 而不是显式抛错。删掉 <c>options.AbortOnConnectFail = true</c> 那行，本测试变红
    /// （拨号"成功"、不再抛异常）。与 Application 侧 CacheMonitorRedisConnectionTests 互补，
    /// 两条连接各自钉住同一纪律。
    /// </summary>
    [Fact]
    public async Task Shared_Connection_Fails_Fast_Even_When_String_Opts_Out_Via_AbortConnectFalse()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(
        [
            new KeyValuePair<string, string?>("Redis:IsEnabled", "true"),
            new KeyValuePair<string, string?>("Redis:Configuration", "127.0.0.1:1,abortConnect=false"),
        ]);
        await using var provider = services.BuildServiceProvider();

        Should.Throw<Exception>(() =>
            provider.GetRequiredService<IDistributedLockProvider>());
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
