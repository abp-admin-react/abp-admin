using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.SignalR;
using Xunit;

namespace AbpAdmin.SignalR;

/// <summary>
/// T3.2 SignalR 注册的静态核对，复用 <see cref="HostUnderTest"/> 的真实宿主服务集合。
/// 不解析任何会连 Redis 的服务：本类只读 ServiceDescriptor 与 Options
/// （IOptions&lt;RedisOptions&gt; 的求值不建连接）。
/// </summary>
/// <remarks>
/// <see cref="HubLifetimeManager{THub}"/> 的两个实现（DefaultHubLifetimeManager /
/// RedisHubLifetimeManager）都是 internal，无法 typeof 引用，只能按类型名断言；
/// 名字本身就是"后注册者胜出"链条的核对点：AddSignalRCore 用 TryAdd 注册 Default，
/// AddStackExchangeRedis 用 AddSingleton 追加 Redis，解析时取最后一条
/// （05-reference-facts.md 第 13.8 节原先标"推断，待验证"，本类把它钉在注册表层面）。
/// </remarks>
public class SignalRServiceRegistrationTests
{
    private const string DefaultHubLifetimeManagerName = "DefaultHubLifetimeManager`1";
    private const string RedisHubLifetimeManagerName = "RedisHubLifetimeManager`1";

    /// <summary>
    /// 打开 backplane 分支的配置。端口指向无监听的地址：本类不解析会建连的服务，
    /// 若将来有人在本类里解析了会拨号的服务，回环地址会立刻被拒绝而不是挂起。
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] BackplaneEnabledConfiguration =
    [
        new("SignalR:UseRedisBackplane", "true"),
        new("Redis:Configuration", "127.0.0.1:6399")
    ];

    [Fact]
    public async Task Backplane_Off_As_Shipped_Registers_Only_DefaultHubLifetimeManager()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync();

        var descriptors = HubLifetimeManagerDescriptors(services);

        // 出厂配置（SignalR:UseRedisBackplane=false）退化为进程内广播，这是已知且可接受的形态
        descriptors.Count.ShouldBe(1);
        descriptors[0].ImplementationType?.Name.ShouldBe(DefaultHubLifetimeManagerName);
    }

    [Fact]
    public async Task Backplane_On_Appends_RedisHubLifetimeManager_After_Default_So_It_Wins()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(BackplaneEnabledConfiguration);

        var descriptors = HubLifetimeManagerDescriptors(services);

        // TryAdd 的 Default 仍在（AddStackExchangeRedis 不替换、只追加），
        // 最后一条必须是 Redis——MS.DI 与 Autofac 解析单个服务时都取最后注册的
        descriptors.Count.ShouldBe(2);
        descriptors[0].ImplementationType?.Name.ShouldBe(DefaultHubLifetimeManagerName);
        descriptors[^1].ImplementationType?.Name.ShouldBe(RedisHubLifetimeManagerName);
    }

    [Fact]
    public async Task Backplane_On_Derives_ChannelPrefix_From_Environment_Name()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(BackplaneEnabledConfiguration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        // 修法①：多环境共用一台 Redis 时靠前缀隔离。HostUnderTest 把环境钉在 Development，
        // 所以前缀必须是 AbpAdmin:SignalR:Development；绝不能是规格原版那个
        // 必然落空到 Default 的 Redis:InstanceName 派生（本仓库没有该键）。
        // ChannelPrefix 是 RedisChannel 值类型（SE.Redis 2.7+），按字符串比较
        options.Configuration.ChannelPrefix.ToString().ShouldBe("AbpAdmin:SignalR:Development");
    }

    [Fact]
    public async Task Backplane_On_Keeps_Default_Self_Built_Redis_Connection()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(BackplaneEnabledConfiguration);

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;

        // ConnectionFactory 必须保持 null（SignalR 自建 ConnectionMultiplexer），
        // 不允许复用 ABP 缓存/锁那条 multiplexer——性能隔离，订阅背压不能牵连缓存延迟。
        // 对应验收：rg "ConnectionFactory" src/AbpAdmin.HttpApi.Host 无结果。
        options.ConnectionFactory.ShouldBeNull();
    }

    [Fact]
    public async Task Host_Replaces_NullRealTimeNotifier_With_SignalRRealTimeNotifier()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync();

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IRealTimeNotifier))
            .ToList();

        // ReplaceServices 语义：Domain.Shared 扫描进的 Null 注册被移除，
        // 只剩宿主的 SignalR 实现一条
        descriptors.Count.ShouldBe(1);
        descriptors[0].ImplementationType.ShouldBe(typeof(SignalRRealTimeNotifier));
    }

    [Fact]
    public async Task NotificationHub_Is_Registered_For_Auto_Hub_Map_With_Explicit_Route()
    {
        // 已核实（ilspycmd 读 Volo.Abp.Autofac 10.6.0）：services.OnRegistered 回调
        // 不在约定注册时触发，而在 AutofacRegistration.Populate 逐条处理最终
        // ServiceDescriptor 时触发。AbpAspNetCoreSignalRModule 靠它把 Hub 类型收进
        // AbpSignalROptions.Hubs，所以在原始 IServiceCollection 上用 MS.DI
        // BuildServiceProvider() 永远读到空的 Hubs——必须与宿主同容器（Autofac）构建。
        var builder = await HostUnderTest.CreateBuilderAsync(useAutofac: true);

        await using var app = builder.Build();

        var hubs = app.Services.GetRequiredService<IOptions<AbpSignalROptions>>().Value.Hubs;

        // AbpSignalRConventionalRegistrar 注册 + Populate 收集 + 端点阶段统一映射，
        // 无需手写 MapHub；路由必须保持前端硬编码的 /signalr-hubs/notification
        var hub = hubs.SingleOrDefault(h => h.HubType == typeof(NotificationHub));
        hub.ShouldNotBeNull(
            "NotificationHub 没有进入 AbpSignalROptions.Hubs，端点阶段不会被映射。" +
            "检查 AbpAspNetCoreSignalRModule 是否仍在 [DependsOn] 链上、Hub 是否带 [DisableAutoHubMap]。");
        hub.RoutePattern.ShouldBe("/signalr-hubs/notification");
    }

    private static List<ServiceDescriptor> HubLifetimeManagerDescriptors(IServiceCollection services)
    {
        return services
            .Where(d => d.ServiceType == typeof(HubLifetimeManager<>))
            .ToList();
    }
}
