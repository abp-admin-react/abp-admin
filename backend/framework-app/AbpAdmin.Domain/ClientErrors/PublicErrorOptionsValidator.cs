using System.Linq;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 校验 ClientErrorHandling 文化与范围。未知键由 Host 严格绑定拒绝。
/// </summary>
public class PublicErrorOptionsValidator : IValidateOptions<ClientErrorHandlingOptions>, ISingletonDependency
{
    public ValidateOptionsResult Validate(string? name, ClientErrorHandlingOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("ClientErrorHandling options are required.");
        }

        if (options.FallbackCulture != null
            && !ClientErrorCultures.Supported.Contains(options.FallbackCulture))
        {
            return ValidateOptionsResult.Fail(
                $"ClientErrorHandling:FallbackCulture '{options.FallbackCulture}' is not in the supported set.");
        }

        if (options.MaxPublicMessageLength <= 0
            || options.MaxPublicMessageLength > ClientErrorHandlingLimits.AbsoluteMaxPublicMessageLength)
        {
            return ValidateOptionsResult.Fail(
                "ClientErrorHandling:MaxPublicMessageLength must be a positive number within the framework limit.");
        }

        if (options.MaxValidationErrors <= 0
            || options.MaxValidationErrors > ClientErrorHandlingLimits.AbsoluteMaxValidationErrors)
        {
            return ValidateOptionsResult.Fail(
                "ClientErrorHandling:MaxValidationErrors must be a positive number within the framework limit.");
        }

        if (options.MaxPublicParameterLength <= 0
            || options.MaxPublicParameterLength > ClientErrorHandlingLimits.AbsoluteMaxPublicParameterLength)
        {
            return ValidateOptionsResult.Fail(
                "ClientErrorHandling:MaxPublicParameterLength must be a positive number within the framework limit.");
        }

        return ValidateOptionsResult.Success;
    }
}
