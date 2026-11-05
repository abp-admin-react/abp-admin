using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Application.ClientErrors;

/// <summary>
/// 客户端错误应用服务。
/// 用于将 Domain 层的投影结果转换为 Application 层的 ClientFailureDto。
/// </summary>
public class ClientErrorAppService : ApplicationService
{
    private readonly IClientErrorProjectionCore _projectionCore;

    public ClientErrorAppService(IClientErrorProjectionCore projectionCore)
    {
        _projectionCore = projectionCore;
    }

    /// <summary>
    /// 根据输入创建客户端失败结果。
    /// 用于 HTTP 200 任务/批量失败结果。
    /// </summary>
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

        var dto = new ClientFailureDto
        {
            Code = result.PublicCode ?? ClientErrorCodes.OperationFailed,
            Message = result.Message,
            CorrelationId = result.CorrelationId,
        };

        if (result.Data != null && result.Data.Count > 0)
        {
            dto.Data = new Dictionary<string, object?>(result.Data);
        }

        return dto;
    }

    /// <summary>
    /// 根据异常创建客户端失败结果。
    /// </summary>
    public ClientFailureDto CreateFailureFromException(
        Exception exception,
        string? correlationId,
        string? cultureName = null)
    {
        var exceptionData = new Dictionary<string, object?>();

        if (exception.Data != null)
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
            exception is BusinessException bex ? bex.Code : null,
            exceptionData,
            correlationId,
            cultureName);
    }
}

/// <summary>
/// 业务异常基类（简化版本，用于错误处理）。
/// </summary>
public class BusinessException : Exception
{
    public string? Code { get; }

    public BusinessException(string message) : base(message)
    {
    }

    public BusinessException(string code, string message) : base(message)
    {
        Code = code;
    }

    public BusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}