using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class PublicErrorOptionsValidatorTests
{
    private readonly PublicErrorOptionsValidator _validator = new();

    [Fact]
    public void Default_Options_Should_Succeed()
    {
        var result = _validator.Validate(null, new ClientErrorHandlingOptions());
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Unsupported_Fallback_Culture_Should_Fail()
    {
        var result = _validator.Validate(null, new ClientErrorHandlingOptions
        {
            FallbackCulture = "ja",
        });
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FallbackCulture");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ClientErrorHandlingLimits.AbsoluteMaxPublicMessageLength + 1)]
    public void Invalid_Message_Length_Should_Fail(int length)
    {
        var result = _validator.Validate(null, new ClientErrorHandlingOptions
        {
            MaxPublicMessageLength = length,
        });
        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ClientErrorHandlingLimits.AbsoluteMaxValidationErrors + 1)]
    public void Invalid_Validation_Errors_Should_Fail(int count)
    {
        var result = _validator.Validate(null, new ClientErrorHandlingOptions
        {
            MaxValidationErrors = count,
        });
        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Supported_Fallback_Culture_Should_Succeed()
    {
        var result = _validator.Validate(null, new ClientErrorHandlingOptions
        {
            FallbackCulture = "zh-Hans",
        });
        result.Succeeded.ShouldBeTrue();
    }
}
