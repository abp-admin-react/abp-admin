using System;
using System.Reflection;
using Shouldly;
using Xunit;

// 共享 PG 库的隔离前提：本程序集全部用例串行执行（同 Biz.Template.Tests 的理由——
// 用例间靠测试基类 Dispose 里的 Respawn 清表隔离，类级并行会让清表落到并行用例头上）。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AbpAdmin.Ordering;

/// <summary>串行化开关的漂移钉子（破坏隔离前提时以清晰的方式失败）。</summary>
public class OrderingTestPgAssemblyContractTests
{
    [Fact]
    public void Assembly_Must_Disable_Test_Parallelization()
    {
        var behavior = typeof(OrderingTestPgAssemblyContractTests).Assembly
            .GetCustomAttribute<CollectionBehaviorAttribute>();
        behavior.ShouldNotBeNull();
        behavior.DisableTestParallelization.ShouldBeTrue();
    }
}
