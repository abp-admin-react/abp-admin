using AbpAdmin.Biz.Template.Configuration;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

public class BizTemplateClientErrorSampleGuardTests
{
    [Fact]
    public void Enabling_In_Production_Should_Fail()
    {
        var environment = new AbpHostEnvironment { EnvironmentName = "Production" };

        var ex = Should.Throw<AbpInitializationException>(() =>
            BizTemplateClientErrorSampleGuard.EnsureCanEnable(enabled: true, environment));

        ex.Message.ShouldContain("EnableClientErrorSample");
        ex.Message.ShouldContain("Production");
    }

    [Fact]
    public void Enabling_In_Development_Should_Succeed()
    {
        var environment = new AbpHostEnvironment { EnvironmentName = "Development" };
        BizTemplateClientErrorSampleGuard.EnsureCanEnable(enabled: true, environment);
    }

    [Fact]
    public void Enabling_In_Testing_Should_Succeed()
    {
        var environment = new AbpHostEnvironment { EnvironmentName = "Testing" };
        BizTemplateClientErrorSampleGuard.EnsureCanEnable(enabled: true, environment);
    }

    [Fact]
    public void Disabled_In_Production_Should_Succeed()
    {
        var environment = new AbpHostEnvironment { EnvironmentName = "Production" };
        BizTemplateClientErrorSampleGuard.EnsureCanEnable(enabled: false, environment);
    }
}
