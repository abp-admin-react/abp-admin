using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.ClientErrors;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 公开错误目录实现。
/// 实现 IPublicErrorCatalog 契约，提供只读的公开错误定义查询。
/// 经 ABP 约定式注册为单例；定义由所有已注册的 <see cref="IPublicErrorDefinitionProvider"/> 聚合。
/// </summary>
[ExposeServices(typeof(IPublicErrorCatalog))]
public class PublicErrorCatalog : IPublicErrorCatalog, ISingletonDependency
{
    private readonly IReadOnlyCollection<PublicErrorDefinition> _definitions;

    /// <summary>
    /// DI 构造：聚合所有 provider 的定义。DI 唯一公开构造，避免解析歧义。
    /// </summary>
    public PublicErrorCatalog(IEnumerable<IPublicErrorDefinitionProvider> providers)
        : this(Flatten(providers))
    {
    }

    private PublicErrorCatalog(IReadOnlyCollection<PublicErrorDefinition> definitions)
    {
        _definitions = definitions;
    }

    /// <summary>
    /// 测试/手工构造：直接使用给定的定义集合。
    /// </summary>
    public static PublicErrorCatalog FromDefinitions(IEnumerable<PublicErrorDefinition> definitions)
    {
        if (definitions is null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        return new PublicErrorCatalog(definitions.ToList().AsReadOnly());
    }

    private static IReadOnlyCollection<PublicErrorDefinition> Flatten(
        IEnumerable<IPublicErrorDefinitionProvider> providers)
    {
        if (providers is null)
        {
            throw new ArgumentNullException(nameof(providers));
        }

        var flattened = providers
            .SelectMany(p => p.GetDefinitions())
            .ToList();
        PublicErrorCatalogStartupValidator.Validate(flattened);
        return flattened.AsReadOnly();
    }

    public IReadOnlyCollection<PublicErrorDefinition> Definitions => _definitions;

    public PublicErrorDefinition? FindByInternalCode(string internalCode)
    {
        if (string.IsNullOrEmpty(internalCode))
        {
            return null;
        }

        return _definitions.FirstOrDefault(d =>
            string.Equals(d.InternalCode, internalCode, StringComparison.Ordinal));
    }

    public PublicErrorDefinition? FindByPublicCode(string publicCode)
    {
        if (string.IsNullOrEmpty(publicCode))
        {
            return null;
        }

        return _definitions.FirstOrDefault(d =>
            string.Equals(d.PublicCode, publicCode, StringComparison.Ordinal));
    }
}