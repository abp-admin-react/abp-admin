using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.Biz.Template.Configuration;

/// <summary>
/// 第②层（模块部署期配置）契约测试：
/// 证明「模块自带 JSON 随工程走 + ConfigureServices 绑定 + 节名一致」这条链路成立。
/// 复制模块改名的下游，这几个用例红 = 改名漏了 SectionName / JSON 根节 / Content 复制。
/// </summary>
public class BizTemplateConfigBindingTests : BizTemplateTestBase
{
    [Fact]
    public void Options_Should_Bind_From_Module_Baseline_Json()
    {
        var options = GetRequiredService<IOptions<BizTemplateOptions>>().Value;

        // 基线值与 Configuration/BizTemplate.json 一致：
        // 绑定成功 ⇔ Content 复制到了测试输出目录 + 根节名与 SectionName 对齐 + 绑定顺序正确
        options.MaxPageSize.ShouldBe(100);
        options.SeedSampleData.ShouldBeTrue();
    }
}
