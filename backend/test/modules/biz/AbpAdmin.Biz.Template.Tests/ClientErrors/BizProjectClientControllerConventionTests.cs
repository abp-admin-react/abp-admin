using System.Linq;
using System.Reflection;
using AbpAdmin.Biz.Template.Client;
using AbpAdmin.Biz.Template.Client.Controllers;
using AbpAdmin.Biz.Template.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

public class BizProjectClientControllerConventionTests : BizTemplateTestBase
{
    [Fact]
    public void Convention_Should_Remove_Controller_When_Disabled()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(
            new ControllerModel(typeof(BizProjectClientController).GetTypeInfo(), []));

        new BizProjectClientControllerConvention(enabled: false).Apply(application);

        application.Controllers.ShouldBeEmpty();
    }

    [Fact]
    public void Convention_Should_Keep_Controller_When_Enabled()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(
            new ControllerModel(typeof(BizProjectClientController).GetTypeInfo(), []));

        new BizProjectClientControllerConvention(enabled: true).Apply(application);

        application.Controllers.Count.ShouldBe(1);
        application.Controllers[0].ControllerType.ShouldBe(typeof(BizProjectClientController));
    }

    [Fact]
    public void Disabled_Sample_Should_Register_Removing_Convention_And_Hide_Controller()
    {
        GetRequiredService<IOptions<BizTemplateOptions>>().Value.EnableClientErrorSample.ShouldBeFalse();

        var mvc = GetRequiredService<IOptions<MvcOptions>>().Value;
        mvc.Conventions.OfType<BizProjectClientControllerConvention>()
            .ShouldContain(convention => !convention.IsSampleEnabled);

        var descriptors = ServiceProvider.GetService<IActionDescriptorCollectionProvider>();
        if (descriptors is null)
        {
            return;
        }

        descriptors.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Any(descriptor => descriptor.ControllerTypeInfo == typeof(BizProjectClientController))
            .ShouldBeFalse();
    }
}
