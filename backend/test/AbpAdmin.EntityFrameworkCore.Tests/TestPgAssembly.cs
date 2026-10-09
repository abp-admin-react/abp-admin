using System.Reflection;
using System.Runtime.CompilerServices;
using AbpAdmin;
using Shouldly;
using Xunit;

// 共享 PG 库的隔离前提：本程序集全部用例串行执行。用例间靠测试基类 Dispose 里的
// Respawn 清表隔离——若类级并行，清表会落到并行执行中的其他用例头上；启动期
// 的种子重播也会并发竞争（查-后-插竞态）。此前的「每用例一座独立内存库」
// 天然并行安全，换共享真库后以串行换隔离。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 程序集加载即把 Testcontainers PG 的连接串放进宿主覆盖环境变量：
/// <see cref="RequiresHostDatabaseFactAttribute"/> 的发现期门控与测试体取串同源，
/// 真库用例（Quartz 表自举 / 租户连接串校验）随本进程容器走，不依赖外部 PG。
/// 容器起不来时静默——门控按「无覆盖」跳过；模块集成测试会在接线处以明确错误失败。
/// </summary>
internal static class TestPgAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => AbpAdminTestPg.TrySetHostOverrideEnvVar();
}

/// <summary>
/// 串行化开关的漂移钉子：删掉上面的 CollectionBehavior 不会立刻红，
/// 而是变成共享库下偶发的清表竞态（看起来像业务测试在花式 flaky）——
/// 反射钉住属性在，让「破坏隔离前提」以清晰的方式失败。
/// </summary>
public class TestPgAssemblyContractTests
{
    [Fact]
    public void Assembly_Must_Disable_Test_Parallelization()
    {
        var behavior = typeof(TestPgAssemblyContractTests).Assembly
            .GetCustomAttribute<CollectionBehaviorAttribute>();
        behavior.ShouldNotBeNull();
        behavior.DisableTestParallelization.ShouldBeTrue();
    }
}
