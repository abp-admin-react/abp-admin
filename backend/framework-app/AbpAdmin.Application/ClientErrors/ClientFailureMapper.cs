using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ExceptionHandling;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// HTTP 200 任务/批量失败结果映射。不是 ApplicationService，避免被 Auto API 暴露。
/// </summary>
public class ClientFailureMapper : ITransientDependency
{
    private readonly IClientErrorProjectionCore _projectionCore;

    public ClientFailureMapper(IClientErrorProjectionCore projectionCore)
    {
        _projectionCore = projectionCore;
    }

    public ClientFailureDto CreateFailureResult(
        string? internalCode,
        IReadOnlyDictionary<string, object?>? exceptionData,
        string? correlationId,
        string? cultureName = null)
    {
        var input = new PublicErrorProjectionInput
        {
            InternalCode = internalCode,
            ExceptionData = exceptionData,
            CorrelationId = correlationId,
            CultureName = cultureName,
            Audience = ClientErrorAudience.Public,
        };

        var result = _projectionCore.Project(input);

        return new ClientFailureDto
        {
            Code = result.PublicCode ?? ClientErrorCodes.OperationFailed,
            Message = result.Message,
            CorrelationId = result.CorrelationId,
            Data = result.Data.Count == 0
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(result.Data, ClientErrorDataKeys.Comparer),
        };
    }

    public ClientFailureDto CreateFailureFromException(
        Exception exception,
        string? correlationId,
        string? cultureName = null)
    {
        var exceptionData = new Dictionary<string, object?>();
        if (exception.Data is { Count: > 0 })
        {
            foreach (var key in exception.Data.Keys)
            {
                if (key is string keyStr)
                {
                    exceptionData[keyStr] = exception.Data[key];
                }
            }
        }

        return CreateFailureResult(
            exception is IHasErrorCode hasCode ? hasCode.Code : null,
            exceptionData,
            correlationId,
            cultureName);
    }
}
