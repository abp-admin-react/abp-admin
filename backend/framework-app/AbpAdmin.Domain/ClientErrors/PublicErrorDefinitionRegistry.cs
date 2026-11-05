using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.ClientErrors;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 公开错误定义注册表（Domain 层实现）。
/// 实现 IPublicErrorDefinitionProvider 契约，在模块初始化时注册框架基础定义。
/// </summary>
public class PublicErrorDefinitionRegistry : IPublicErrorDefinitionProvider
{
    private readonly List<PublicErrorDefinition> _definitions = new();

    public PublicErrorDefinitionRegistry()
    {
        RegisterBaseDefinitions();
    }

    /// <summary>
    /// 返回已注册的定义集合。
    /// </summary>
    public IReadOnlyList<PublicErrorDefinition> GetDefinitions() => _definitions.AsReadOnly();

    /// <summary>
    /// 注册单个定义。
    /// </summary>
    public void Register(PublicErrorDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.InternalCode))
        {
            throw new ArgumentException("InternalCode is required.", nameof(definition));
        }

        if (string.IsNullOrEmpty(definition.PublicCode))
        {
            throw new ArgumentException("PublicCode is required.", nameof(definition));
        }

        if (_definitions.Any(d => d.InternalCode == definition.InternalCode))
        {
            throw new InvalidOperationException(
                $"Duplicate internal code: {definition.InternalCode}");
        }

        if (_definitions.Any(d => d.PublicCode == definition.PublicCode))
        {
            throw new InvalidOperationException(
                $"Duplicate public code: {definition.PublicCode}");
        }

        _definitions.Add(definition);
    }

    private void RegisterBaseDefinitions()
    {
        var baseDefinitions = new[]
        {
            new PublicErrorDefinition
            {
                InternalCode = "Client:AuthenticationRequired",
                PublicCode = ClientErrorCodes.AuthenticationRequired,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "AuthenticationRequired",
                Kind = PublicErrorKind.Authentication,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:Forbidden",
                PublicCode = ClientErrorCodes.Forbidden,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "Forbidden",
                Kind = PublicErrorKind.Forbidden,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:ValidationFailed",
                PublicCode = ClientErrorCodes.ValidationFailed,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "ValidationFailed",
                Kind = PublicErrorKind.Validation,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:NotFound",
                PublicCode = ClientErrorCodes.NotFound,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "NotFound",
                Kind = PublicErrorKind.NotFound,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:RateLimited",
                PublicCode = ClientErrorCodes.RateLimited,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "RateLimited",
                Kind = PublicErrorKind.RateLimited,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:Conflict",
                PublicCode = ClientErrorCodes.Conflict,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "Conflict",
                Kind = PublicErrorKind.Conflict,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:OperationFailed",
                PublicCode = ClientErrorCodes.OperationFailed,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "OperationFailed",
                Kind = PublicErrorKind.Business,
            },
            new PublicErrorDefinition
            {
                InternalCode = "Client:Unexpected",
                PublicCode = ClientErrorCodes.Unexpected,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "Unexpected",
                Kind = PublicErrorKind.Server,
            },
        };

        foreach (var definition in baseDefinitions)
        {
            _definitions.Add(definition);
        }
    }

    IEnumerable<PublicErrorDefinition> IPublicErrorDefinitionProvider.GetDefinitions()
    {
        return GetDefinitions();
    }
}

/// <summary>
/// 客户端错误静态资源（临时占位，实际资源由 Localization 提供）。
/// 实现 IClientStaticResource 标记接口。
/// </summary>
public class ClientErrorResource : IClientStaticResource
{
}