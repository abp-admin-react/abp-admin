using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class PublicErrorCatalogStartupValidatorTests
{
    [Fact]
    public void Duplicate_Internal_Code_Must_Fail()
    {
        var definition = CreateDefinition("Biz:Unavailable", "Biz:Public:Unavailable");
        var exception = Should.Throw<AbpInitializationException>(
            () => PublicErrorCatalogStartupValidator.Validate([definition, definition]));
        exception.Message.ShouldContain("Duplicate internal error code");
    }

    [Fact]
    public void Inconsistent_Public_Code_Must_Fail()
    {
        var first = CreateDefinition("Biz:Unavailable", "Biz:Public:Unavailable");
        var second = first with
        {
            InternalCode = "Other:Unavailable",
            MessageKey = "OtherKey",
        };

        var exception = Should.Throw<AbpInitializationException>(
            () => PublicErrorCatalogStartupValidator.Validate([first, second]));
        exception.Message.ShouldContain("Inconsistent public error definition");
    }

    [Fact]
    public void Consistent_Shared_Public_Code_May_Succeed()
    {
        var first = CreateDefinition("Biz:Unavailable", "Biz:Public:Unavailable");
        var second = first with { InternalCode = "Other:Unavailable" };

        PublicErrorCatalogStartupValidator.Validate([first, second]);
    }

    private static PublicErrorDefinition CreateDefinition(string internalCode, string publicCode)
    {
        return new PublicErrorDefinition
        {
            InternalCode = internalCode,
            PublicCode = publicCode,
            ResourceType = typeof(ClientErrorResource),
            MessageKey = "Unavailable",
            Kind = PublicErrorKind.Business,
        };
    }
}
