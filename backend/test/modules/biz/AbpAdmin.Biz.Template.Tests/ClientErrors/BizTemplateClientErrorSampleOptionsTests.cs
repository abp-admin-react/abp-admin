using AbpAdmin.Biz.Template.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

public class BizTemplateClientErrorSampleOptionsTests : BizTemplateTestBase
{
    [Fact]
    public void EnableClientErrorSample_Should_Default_To_False_On_Options_Type()
    {
        new BizTemplateOptions().EnableClientErrorSample.ShouldBeFalse();
    }

    [Fact]
    public void EnableClientErrorSample_Should_Bind_False_From_Module_Baseline_Json()
    {
        var options = GetRequiredService<IOptions<BizTemplateOptions>>().Value;
        options.EnableClientErrorSample.ShouldBeFalse();
    }
}
