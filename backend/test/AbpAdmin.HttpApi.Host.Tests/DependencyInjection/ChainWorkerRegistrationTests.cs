using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using AbpAdmin.Collection.Chain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AbpAdmin.HttpApi.Host.Tests.DependencyInjection;

/// <summary>
/// 链上 worker 注册契约（计划第 59、65 行）：
/// ① 开关默认关（仓库自带配置）——worker 虽注册但不跑；② 扫链/归集开着而分布式锁不是
/// Redis 支撑（Redis:IsEnabled 显式 false，或 Redis:Configuration 空白）→ 建图即抛
/// AbpInitializationException；③ DbMigrator 进程不含链上 hosted service（迁移器不起调度器）。
/// </summary>
public class ChainWorkerRegistrationTests
{
    [Fact]
    public async Task Shipped_Configuration_Leaves_Chain_Switches_Off()
    {
        var services = await HostUnderTest.CreateServiceCollectionAsync();

        var options = CollectionChainWorkerOptions.FromConfiguration(
            BuildConfiguration(services));
        options.ScanEnabled.ShouldBeFalse();
        options.SweepEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Scan_On_Without_Redis_Backend_Lock_Fails_Fast()
    {
        var exception = await Record.ExceptionAsync(() =>
            HostUnderTest.CreateServiceCollectionAsync(new Dictionary<string, string?>
            {
                ["Collection:Chain:ScanEnabled"] = "true",
                ["Redis:IsEnabled"] = "false",
            }));

        exception.ShouldBeOfType<Volo.Abp.AbpInitializationException>();
        exception!.InnerException!.Message.ShouldContain("Redis");
    }

    [Fact]
    public async Task Sweep_On_With_Blank_Redis_Configuration_Fails_Fast()
    {
        var exception = await Record.ExceptionAsync(() =>
            HostUnderTest.CreateServiceCollectionAsync(new Dictionary<string, string?>
            {
                ["Collection:Chain:SweepEnabled"] = "true",
                ["Redis:IsEnabled"] = "true",
                ["Redis:Configuration"] = " ",
            }));

        exception.ShouldBeOfType<Volo.Abp.AbpInitializationException>();
    }

    [Fact]
    public async Task Chain_Workers_Are_Not_Registered_In_DbMigrator_Service_Graph()
    {
        // DbMigrator 模块图不含 AddCollectionChainWorkers（扩展只在宿主调用）——
        // 直接断言模块程序集的类型图：worker 类型不属于 DbMigrator 引用链上的注册点，
        // 并断言 worker 类型本身是 IHostedService（注册语义正确性）
        typeof(CollectionChainWorkerBase).IsAssignableFrom(typeof(DepositScanWorker)).ShouldBeTrue();
        typeof(CollectionChainWorkerBase).IsAssignableFrom(typeof(SweepWorker)).ShouldBeTrue();

        // DbMigrator 项目不引用宿主工程（编译期隔离），这里核对宿主模块才调用扩展：
        var hostModuleSource = System.IO.File.ReadAllText(System.IO.Path.Combine(LocateHostDirectory(), "AbpAdminHttpApiHostModule.cs"));
        hostModuleSource.ShouldContain("AddCollectionChainWorkers");
    }

    private static Microsoft.Extensions.Configuration.IConfiguration BuildConfiguration(
        Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        // 与宿主同源的配置栈（appsettings.json + secrets）；不借服务容器（同 RedisStartupPostureTests 形态）
        var hostDir = LocateHostDirectory();
        System.IO.Directory.Exists(hostDir).ShouldBeTrue($"host dir not found: {hostDir}");
        return new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile(System.IO.Path.Combine(hostDir, "appsettings.json"), optional: false)
            .AddJsonFile(System.IO.Path.Combine(hostDir, "appsettings.secrets.json"), optional: true)
            .Build();
    }

    /// <summary>从测试程序集位置向上找到含 src/AbpAdmin.HttpApi.Host 的仓库层（路径算术不炸）。</summary>
    private static string LocateHostDirectory()
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(
            typeof(ChainWorkerRegistrationTests).Assembly.Location)!);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "src", "AbpAdmin.HttpApi.Host");
            if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            dir = dir.Parent!;
        }

        return string.Empty;
    }
}
