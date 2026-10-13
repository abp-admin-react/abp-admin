using System.IO;
using System.Linq;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 退款守卫的框架侧纯度（计划第 2 节）：AbpAdmin.Application 的源码里不出现模块名——
/// 框架只定义 IPaymentRefundGuard 并在退款前询问全部实现；收款模块注册自己的实现。
/// 守卫测试断言的就是这一点。
/// </summary>
public class RefundGuardSourcePurityTests
{
    [Fact]
    public void Application_Source_Does_Not_Mention_Module_Names()
    {
        var repoRoot = FindRepoRoot(Path.GetDirectoryName(typeof(RefundGuardSourcePurityTests).Assembly.Location)!);
        var sourceDir = Path.Combine(repoRoot, "framework", "AbpAdmin.Application");

        var offenders = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("AbpAdmin.Collection") || File.ReadAllText(f).Contains("AbpAdmin.Ordering"))
            .ToList();

        offenders.ShouldBeEmpty(
            "AbpAdmin.Application 的源码里出现了收款/订单模块名——退款拦截只能经 IPaymentRefundGuard 扩展点，" +
            "框架侧不得反向依赖业务模块。命中文件：" + string.Join(", ", offenders));
    }

    [Fact]
    public void Refund_Guard_Interface_Lives_In_Domain_Shared()
    {
        var interfaceType = typeof(AbpAdmin.Payments.IPaymentRefundGuard);
        interfaceType.Assembly.GetName().Name.ShouldBe("AbpAdmin.Domain.Shared");
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AbpAdmin.slnx")))
        {
            dir = dir.Parent!;
        }

        dir.ShouldNotBeNull("未能从测试程序集位置定位仓库根（AbpAdmin.slnx）");
        return dir.FullName;
    }
}
