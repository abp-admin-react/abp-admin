using System;
using AbpAdmin.Biz.Template.Data;
using Shouldly;
using Xunit;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 设计时工厂路径哨兵（机制说明见 AbpAdmin.EntityFrameworkCore.Tests 同名测试）：
/// 工厂从宿主 appsettings.json（optional: false）起链解析配置，分区/目录迁移后旧路径
/// 会让 ConfigurationBuilder 直接抛 FileNotFoundException 且 dotnet test 不触该链路。
/// 合格判定：CreateDbContext 成功，或仅抛文档化的"连接串缺失" InvalidOperationException。
/// 随样板模块复制：新增业务模块的测试工程继承本用例即继承路径哨兵。
/// </summary>
public class DesignTimeDbContextFactoryTests
{
    [Fact]
    public void BizTemplate_Factory_Resolves_Configuration_Without_Path_Rot()
    {
        var ex = Record.Exception(() => new BizTemplateDbContextFactory().CreateDbContext(Array.Empty<string>()));

        if (ex == null)
        {
            return;
        }

        ex.ShouldBeOfType<InvalidOperationException>();
        ex.Message.ShouldContain("ConnectionStrings:Default");
    }
}
