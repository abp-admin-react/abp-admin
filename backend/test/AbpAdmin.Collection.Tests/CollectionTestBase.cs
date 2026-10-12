using System.Globalization;
using System.Reflection;
using Shouldly;
using Xunit;

// 共享 PG 库的隔离前提：本程序集全部用例串行执行（同 Biz.Template.Tests 的理由）
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AbpAdmin.Collection;

/// <summary>串行化开关的漂移钉子。</summary>
public class CollectionTestPgAssemblyContractTests
{
    [Fact]
    public void Assembly_Must_Disable_Test_Parallelization()
    {
        var behavior = typeof(CollectionTestPgAssemblyContractTests).Assembly
            .GetCustomAttribute<CollectionBehaviorAttribute>();
        behavior.ShouldNotBeNull();
        behavior.DisableTestParallelization.ShouldBeTrue();
    }
}

/// <summary>收款模块测试基类：真迁移建表（框架上下文 + Collection 上下文），en 文化固定。</summary>
public abstract class CollectionTestBase : AbpAdminTestBase<CollectionTestModule>
{
    protected CollectionTestBase()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        // 静态假件跨应用重建存活：每用例复位，避免上一个用例的预配泄漏
        Fakes.FakeChannelGateway.Wechat.Reset();
        Fakes.FakeChannelGateway.Alipay.Reset();
    }
}
