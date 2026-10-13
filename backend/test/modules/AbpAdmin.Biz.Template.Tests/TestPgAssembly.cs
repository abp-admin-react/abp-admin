using System.Reflection;
using Shouldly;
using Xunit;

// 共享 PG 库的隔离前提：本程序集全部用例串行执行。用例间靠测试基类 Dispose 里的
// Respawn 清表隔离——若类级并行，清表会落到并行执行中的其他用例头上；启动期
// 的种子重播也会并发竞争（查-后-插竞态）。此前的「每用例一座独立内存库」
// 天然并行安全，换共享真库后以串行换隔离。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AbpAdmin.Biz.Template;

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
