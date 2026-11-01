using System;
using AbpAdmin.Webhooks.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 设计时工厂路径哨兵：工厂从宿主 appsettings.json（optional: false）起链解析配置，
/// 分区/目录迁移后旧路径会让 ConfigurationBuilder 直接抛 FileNotFoundException——
/// 而 dotnet test 全程不触设计时链路（运行时走模块化配置），没有本测试该类损坏静默漏网。
/// 合格判定：CreateDbContext 成功（基座样本串可解析）；或仅抛文档化的"连接串缺失"
/// InvalidOperationException（全新克隆无 Database.json/secrets）。其余异常 = 路径腐坏。
/// </summary>
public class DesignTimeDbContextFactoryTests
{
    [Fact]
    public void AbpAdmin_Factory_Resolves_Configuration_Without_Path_Rot()
    {
        var ex = Record.Exception(() => new AbpAdminDbContextFactory().CreateDbContext(Array.Empty<string>()));
        AssertPathRotOnly(ex);
    }

    [Fact]
    public void Webhooks_Factory_Resolves_Configuration_Without_Path_Rot()
    {
        var ex = Record.Exception(() => new WebhooksDbContextFactory().CreateDbContext(Array.Empty<string>()));
        AssertPathRotOnly(ex);
    }

    private static void AssertPathRotOnly(Exception? ex)
    {
        if (ex == null)
        {
            return;
        }

        ex.ShouldBeOfType<InvalidOperationException>();
        ex.Message.ShouldContain("ConnectionStrings:Default");
    }
}
