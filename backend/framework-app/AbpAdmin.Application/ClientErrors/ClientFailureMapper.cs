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

    /// <summary>按内部码投影 HTTP 200 任务失败结果。Data 只含批准键。</summary>
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

    /// <summary>
    /// 丢弃原文，走 OperationFailed 公开码。rawMessage 不得进入客户响应。
    /// </summary>
    public ClientFailureDto CreateFailureFromMessage(
        string? rawMessage,
        string? correlationId,
        string? cultureName = null)
    {
        _ = rawMessage;
        return CreateFailureResult(
            ClientErrorCodes.OperationFailed,
            exceptionData: null,
            correlationId,
            cultureName);
    }

    /// <summary>从异常取内部码和 Data 快照，再走公开目录。异常原文不回传。</summary>
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
