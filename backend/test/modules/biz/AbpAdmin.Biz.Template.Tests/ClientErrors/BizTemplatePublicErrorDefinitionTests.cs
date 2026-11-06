using System.Linq;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.ClientErrors;
using Shouldly;
using Xunit;

namespace AbpAdmin.Biz.Template.ClientErrors;

public class BizTemplatePublicErrorDefinitionTests : BizTemplateTestBase
{
    [Fact]
    public void Public_Catalog_Should_Register_ProjectUnavailable()
    {
        var catalog = GetRequiredService<IPublicErrorCatalog>();

        var definition = catalog.FindByInternalCode(BizTemplateClientErrorCodes.ProjectUnavailable);
        definition.ShouldNotBeNull();
        definition!.PublicCode.ShouldBe(BizTemplateClientErrorCodes.ProjectUnavailablePublic);
        definition.ResourceType.ShouldBe(typeof(BizTemplateClientResource));
        definition.MessageKey.ShouldBe("ProjectUnavailable");
        definition.Kind.ShouldBe(PublicErrorKind.Business);
        definition.AllowedParameters.ShouldBeEmpty();
    }

    [Fact]
    public void Provider_Should_Declare_The_Same_Code()
    {
        var providers = GetRequiredService<System.Collections.Generic.IEnumerable<IPublicErrorDefinitionProvider>>();
        var definitions = providers.SelectMany(provider => provider.GetDefinitions()).ToList();

        definitions.ShouldContain(definition =>
            definition.InternalCode == BizTemplateClientErrorCodes.ProjectUnavailable
            && definition.PublicCode == BizTemplateClientErrorCodes.ProjectUnavailablePublic);
    }
}
