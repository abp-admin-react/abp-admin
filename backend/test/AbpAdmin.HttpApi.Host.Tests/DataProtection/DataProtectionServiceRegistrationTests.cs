using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.DataProtection;

/// <summary>
/// DataProtection Redis 持久化注册的静态核对，复用 <see cref="HostUnderTest"/> 的真实宿主
/// 服务集合，与 SignalRServiceRegistrationTests 同一惯例。PersistKeysToStackExchangeRedis
/// 不注册 IXmlRepository 服务，而是把 RedisXmlRepository 写进
/// IOptions&lt;KeyManagementOptions&gt;.Value.XmlRepository；Options 求值只构造仓储对象
/// （存下 ConfigureRedis 的惰性 GetDatabase 委托），真正连 Redis 发生在密钥环首次读写——
/// 「建图零拨号」契约不被打破（端口指向无监听地址，若有解析路径真的拨号会立刻被拒绝）。
/// </summary>
public class DataProtectionServiceRegistrationTests
{
    /// <summary>
    /// 打开 Redis 分支的配置，与 HostInitializationLogTests 的 RedisEnabledConfiguration
    /// 同一纪律：地址明显为假（特权端口、无监听），使任何意外的建连立刻显性失败。
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] RedisEnabledConfiguration =
    [
        new("Redis:IsEnabled", "true"),
        new("Redis:Configuration", "127.0.0.1:1")
    ];

    [Fact]
    public void Key_Name_Isolates_Environments()
    {
        // env 后缀一旦丢失（键名固定、跨环境共享），dev 与 prod 的签名密钥就互相可见、
        // 令牌互相可验——ConfigureRedis 注释点名的跨环境互验漏洞，用红测试钉死格式。
        AbpAdminHttpApiHostModule.BuildDataProtectionKeyName("Development")
            .ShouldBe("AbpAdmin:DataProtection-Keys:Development");
        AbpAdminHttpApiHostModule.BuildDataProtectionKeyName("Production")
            .ShouldBe("AbpAdmin:DataProtection-Keys:Production");
    }

    [Fact]
    public async Task Redis_Enabled_Switches_Key_Repository_To_Redis()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync(RedisEnabledConfiguration);
        await using var provider = services.BuildServiceProvider();

        var repository = provider.GetRequiredService<IOptions<KeyManagementOptions>>()
            .Value.XmlRepository;

        // 持久化没接线时 XmlRepository 为 null（框架默认在 OptionsSetup 里兜底文件系统），
        // 这里必须显式是 Redis 仓储；包内类型不公开，按名断言（与 HubLifetimeManager 同法）
        repository.ShouldNotBeNull();
        repository.GetType().Name.ShouldBe("RedisXmlRepository");

        // 接线细节一并钉死（只断言仓储类型，调用点退化成硬编码 env-less 键名时类型仍匹配）：
        // 键名必须来自 BuildDataProtectionKeyName(环境名)——HostUnderTest 把环境钉在 Development，
        // RedisXmlRepository 把构造参数存进私有字段 _key（包源码形态，升级包时本断言会先红）
        var keyField = repository.GetType().GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance);
        keyField.ShouldNotBeNull();
        // 包内把键名存为 RedisKey 值类型（私有字段 _key），按字符串比较
        keyField.GetValue(repository)!.ToString().ShouldBe(
            AbpAdminHttpApiHostModule.BuildDataProtectionKeyName("Development"));

        // SetApplicationName 固定为 "AbpAdmin"：默认取 content root 路径，镜像工作目录不同或
        // 滚动升级换目录时各实例会各发各的密钥环，集中持久化形同虚设
        // SetApplicationName 落在 ApplicationDiscriminator 上
        provider.GetRequiredService<IOptions<DataProtectionOptions>>()
            .Value.ApplicationDiscriminator.ShouldBe("AbpAdmin");
    }

    [Fact]
    public async Task Redis_Disabled_As_Shipped_Keeps_Repository_Off_Redis()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync();
        await using var provider = services.BuildServiceProvider();

        var repository = provider.GetRequiredService<IOptions<KeyManagementOptions>>()
            .Value.XmlRepository;

        // 出厂配置（Redis:IsEnabled=false）不得挂 Redis 仓储——密钥留在文件系统是单实例
        // 默认姿态；不断言具体默认实现（框架 OptionsSetup 兜底），只钉"不是 Redis"
        repository?.GetType().Name.ShouldNotBe("RedisXmlRepository");
    }
}
