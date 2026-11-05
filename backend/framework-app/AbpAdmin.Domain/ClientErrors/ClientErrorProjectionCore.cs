using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.ClientErrors;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 客户端错误投影核心实现。
/// 实现 IClientErrorProjectionCore 契约，将异常/输入投影为安全的公开响应。
/// 
/// 注意：此实现使用简化的本地化回退。完整本地化由 P2 资源隔离阶段处理。
/// </summary>
public class ClientErrorProjectionCore : IClientErrorProjectionCore
{
    private readonly IPublicErrorCatalog _catalog;

    public ClientErrorProjectionCore(IPublicErrorCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public PublicErrorProjectionResult Project(PublicErrorProjectionInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        string? publicCode = null;
        PublicErrorKind kind = PublicErrorKind.Business;
        string message;
        IReadOnlyDictionary<string, object?> data;
        IReadOnlyList<PublicValidationErrorItem> validationErrors = [];
        int? retryAfterSeconds = input.RetryAfterSeconds;

        var definition = !string.IsNullOrEmpty(input.InternalCode)
            ? _catalog.FindByInternalCode(input.InternalCode)
            : null;

        if (definition != null)
        {
            publicCode = definition.PublicCode;
            kind = definition.Kind;
            message = GetFallbackMessage(definition.MessageKey);
            data = FilterData(input.ExceptionData, definition);
            validationErrors = BuildValidationErrors(definition);
        }
        else if (!string.IsNullOrEmpty(input.InternalCode))
        {
            publicCode = ClientErrorCodes.OperationFailed;
            kind = PublicErrorKind.Business;
            message = GetFallbackMessage("OperationFailed");
            data = new Dictionary<string, object?>();
        }
        else
        {
            publicCode = ClientErrorCodes.Unexpected;
            kind = PublicErrorKind.Server;
            message = GetFallbackMessage("Unexpected");
            data = new Dictionary<string, object?>();
        }

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
            _ => "An error occurred. Please try again.",
        };
    }

    private IReadOnlyDictionary<string, object?> FilterData(
        IReadOnlyDictionary<string, object?>? exceptionData,
        PublicErrorDefinition definition)
    {
        if (exceptionData == null || exceptionData.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var approvedFields = BuildApprovedFieldSet(definition);
        return approvedFields.Filter(exceptionData);
    }

    private ApprovedDataFieldSet BuildApprovedFieldSet(PublicErrorDefinition definition)
    {
        var fieldNames = new List<string>();

        if (definition.AllowedParameters != null)
        {
            foreach (var param in definition.AllowedParameters)
            {
                if (param.CanReturnToClient)
                {
                    fieldNames.Add(param.Name);
                }
            }
        }

        if (fieldNames.Count == 0)
        {
            return ApprovedDataFieldSet.Empty;
        }

        return new ApprovedDataFieldSet(fieldNames);
    }

    private IReadOnlyList<PublicValidationErrorItem> BuildValidationErrors(
        PublicErrorDefinition definition)
    {
        if (definition.ValidationRules == null || definition.ValidationRules.Count == 0)
        {
            return [];
        }

        var errors = new List<PublicValidationErrorItem>();

        foreach (var rule in definition.ValidationRules)
        {
            var members = new List<string> { rule.PublicFieldName };
            errors.Add(new PublicValidationErrorItem
            {
                Message = GetFallbackMessage(rule.MessageKey),
                Members = members,
            });
        }

        return errors;
    }
}