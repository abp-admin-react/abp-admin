using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AbpAdmin.ClientErrors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 将内部错误投影为公开码、安全文案和批准 data。不修改输入异常。
/// </summary>
[ExposeServices(typeof(IClientErrorProjectionCore))]
public class ClientErrorProjectionCore : IClientErrorProjectionCore, ISingletonDependency
{
    internal const string OversizedMessageFallback = ClientErrorCodes.SafeFallbackMessage;

    private readonly IPublicErrorCatalog _catalog;
    private readonly ClientErrorHandlingOptions _options;
    private readonly IStringLocalizerFactory? _localizerFactory;

    public ClientErrorProjectionCore(IPublicErrorCatalog catalog)
        : this(catalog, Microsoft.Extensions.Options.Options.Create(new ClientErrorHandlingOptions()), localizerFactory: null!)
    {
        _localizerFactory = null;
    }

    [ActivatorUtilitiesConstructor]
    public ClientErrorProjectionCore(
        IPublicErrorCatalog catalog,
        IOptions<ClientErrorHandlingOptions> options,
        IStringLocalizerFactory localizerFactory)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options?.Value ?? new ClientErrorHandlingOptions();
        _localizerFactory = localizerFactory;
    }

    public PublicErrorProjectionResult Project(PublicErrorProjectionInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var definition = !string.IsNullOrEmpty(input.InternalCode)
            ? _catalog.FindByInternalCode(input.InternalCode)
            : null;

        string? publicCode;
        PublicErrorKind kind;
        string message;
        IReadOnlyList<PublicValidationErrorItem> validationErrors = [];
        var retryAfterSeconds = input.RetryAfterSeconds;

        if (definition != null)
        {
            publicCode = definition.PublicCode;
            kind = definition.Kind;
            message = ResolveMessage(definition, input.CultureName);
            validationErrors = BuildValidationErrors(definition, input.CultureName);
        }
        else if (!string.IsNullOrEmpty(input.InternalCode))
        {
            publicCode = ClientErrorCodes.OperationFailed;
            kind = PublicErrorKind.Business;
            message = ResolveFallbackKey("OperationFailed", input.CultureName);
        }
        else
        {
            publicCode = ClientErrorCodes.Unexpected;
            kind = PublicErrorKind.Server;
            message = ResolveFallbackKey("Unexpected", input.CultureName);
        }

        var data = BuildData(input, definition);
        message = EnforceMessageLimit(message);
        validationErrors = EnforceValidationErrorsLimit(validationErrors);

        return new PublicErrorProjectionResult
        {
            PublicCode = publicCode,
            Kind = kind,
            Message = message,
            Data = data,
            ValidationErrors = validationErrors,
            RetryAfterSeconds = retryAfterSeconds,
            CorrelationId = input.CorrelationId,
        };
    }

    internal string EnforceMessageLimit(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return OversizedMessageFallback;
        }

        return message.Length > _options.MaxPublicMessageLength
            ? OversizedMessageFallback
            : message;
    }

    internal IReadOnlyList<PublicValidationErrorItem> EnforceValidationErrorsLimit(
        IReadOnlyList<PublicValidationErrorItem> validationErrors)
    {
        if (validationErrors.Count <= _options.MaxValidationErrors)
        {
            return validationErrors;
        }

        return validationErrors.Take(_options.MaxValidationErrors).ToList();
    }

    private IReadOnlyDictionary<string, object?> BuildData(
        PublicErrorProjectionInput input,
        PublicErrorDefinition? definition)
    {
        var data = new Dictionary<string, object?>(ClientErrorDataKeys.Comparer);

        if (_options.IncludeCorrelationId && !string.IsNullOrEmpty(input.CorrelationId))
        {
            data[ClientErrorFieldNames.CorrelationId] = input.CorrelationId;
        }

        if (definition?.AllowedParameters == null)
        {
            return data;
        }

        foreach (var param in definition.AllowedParameters)
        {
            if (!param.CanReturnToClient)
            {
                continue;
            }

            if (!TryReadParameter(param, input, out var value))
            {
                continue;
            }

            data[param.Name] = value;
        }

        if (input.Audience == ClientErrorAudience.AdminCompatibility
            && data.TryGetValue(ClientErrorFieldNames.RetryAfterSeconds, out var retry)
            && !data.ContainsKey(ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds))
        {
            data[ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds] = retry;
        }

        return data;
    }

    private bool TryReadParameter(
        PublicErrorParameterDefinition param,
        PublicErrorProjectionInput input,
        out object? value)
    {
        value = null;
        object? raw = param.Source switch
        {
            PublicErrorParameterSource.CorrelationId => input.CorrelationId,
            PublicErrorParameterSource.RetryAfterSeconds => input.RetryAfterSeconds,
            PublicErrorParameterSource.ExceptionData => ReadExceptionData(input.ExceptionData, param),
            _ => null,
        };

        if (raw is null)
        {
            return false;
        }

        try
        {
            switch (param.ValueType)
            {
                case PublicErrorParameterValueType.String:
                    var text = raw as string ?? raw.ToString();
                    if (string.IsNullOrEmpty(text) || text.Length > _options.MaxPublicParameterLength)
                    {
                        return false;
                    }

                    if (param.MaxLength is int maxLength && text.Length > maxLength)
                    {
                        return false;
                    }

                    value = text;
                    return true;

                case PublicErrorParameterValueType.Int32:
                    if (!TryToInt64(raw, out var i32) || i32 < int.MinValue || i32 > int.MaxValue)
                    {
                        return false;
                    }

                    if (param.MinValue is long min32 && i32 < min32)
                    {
                        return false;
                    }

                    if (param.MaxValue is long max32 && i32 > max32)
                    {
                        return false;
                    }

                    value = (int)i32;
                    return true;

                case PublicErrorParameterValueType.Int64:
                    if (!TryToInt64(raw, out var i64))
                    {
                        return false;
                    }

                    if (param.MinValue is long min64 && i64 < min64)
                    {
                        return false;
                    }

                    if (param.MaxValue is long max64 && i64 > max64)
                    {
                        return false;
                    }

                    value = i64;
                    return true;

                case PublicErrorParameterValueType.Boolean:
                    if (raw is bool b)
                    {
                        value = b;
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static object? ReadExceptionData(
        IReadOnlyDictionary<string, object?>? exceptionData,
        PublicErrorParameterDefinition param)
    {
        if (exceptionData == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(param.SourceKey)
            && exceptionData.TryGetValue(param.SourceKey, out var bySource)
            && bySource != null)
        {
            return bySource;
        }

        if (exceptionData.TryGetValue(param.Name, out var byName))
        {
            return byName;
        }

        return null;
    }

    private static bool TryToInt64(object raw, out long value)
    {
        switch (raw)
        {
            case long l:
                value = l;
                return true;
            case int i:
                value = i;
                return true;
            case short s:
                value = s;
                return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                value = (long)Math.Clamp(Math.Ceiling(d), long.MinValue, long.MaxValue);
                return true;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                value = (long)Math.Clamp(Math.Ceiling((double)f), long.MinValue, long.MaxValue);
                return true;
            case decimal m:
                value = (long)Math.Clamp(Math.Ceiling(m), long.MinValue, long.MaxValue);
                return true;
            case string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private string ResolveMessage(PublicErrorDefinition definition, string? cultureName)
    {
        return Localize(definition.ResourceType, definition.MessageKey, cultureName)
               ?? GetFallbackMessage(definition.MessageKey);
    }

    private string ResolveFallbackKey(string messageKey, string? cultureName)
    {
        return Localize(typeof(ClientErrorResource), messageKey, cultureName)
               ?? GetFallbackMessage(messageKey);
    }

    private string? Localize(Type resourceType, string messageKey, string? cultureName)
    {
        if (_localizerFactory == null)
        {
            return null;
        }

        try
        {
            var localizer = _localizerFactory.Create(resourceType);
            foreach (var culture in BuildCultureChain(cultureName))
            {
                using (CultureHelper.Use(culture))
                {
                    var localized = localizer[messageKey];
                    if (localized.ResourceNotFound
                        || string.IsNullOrWhiteSpace(localized.Value)
                        || string.Equals(localized.Value, messageKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return localized.Value;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private IEnumerable<string> BuildCultureChain(string? cultureName)
    {
        if (!string.IsNullOrEmpty(cultureName))
        {
            yield return cultureName;
        }

        if (!string.IsNullOrEmpty(_options.FallbackCulture))
        {
            yield return _options.FallbackCulture;
        }

        foreach (var supported in ClientErrorCultures.Supported)
        {
            yield return supported;
        }
    }

    private static string GetFallbackMessage(string messageKey)
    {
        return messageKey switch
        {
            "AuthenticationRequired" => "Authentication required. Please log in.",
            "Forbidden" => "You do not have permission to perform this action.",
            "ValidationFailed" => "The input validation failed.",
            "NotFound" => "The requested resource was not found.",
            "RateLimited" => "Too many requests. Please try again later.",
            "Conflict" => "The resource has been modified. Please refresh and try again.",
            "OperationFailed" => "The operation failed. Please try again.",
            "Unexpected" => "An unexpected error occurred. Please try again later.",
            _ => OversizedMessageFallback,
        };
    }

    private IReadOnlyList<PublicValidationErrorItem> BuildValidationErrors(
        PublicErrorDefinition definition,
        string? cultureName)
    {
        if (definition.ValidationRules == null || definition.ValidationRules.Count == 0)
        {
            return [];
        }

        var errors = new List<PublicValidationErrorItem>();
        foreach (var rule in definition.ValidationRules)
        {
            errors.Add(new PublicValidationErrorItem
            {
                Message = ResolveFallbackKey(rule.MessageKey, cultureName),
                Members = [rule.PublicFieldName],
            });
        }

        return errors;
    }
}
