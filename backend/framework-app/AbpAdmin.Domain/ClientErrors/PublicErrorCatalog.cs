using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.ClientErrors;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 公开错误目录实现。
/// 实现 IPublicErrorCatalog 契约，提供只读的公开错误定义查询。
/// </summary>
public class PublicErrorCatalog : IPublicErrorCatalog
{
    private readonly IReadOnlyCollection<PublicErrorDefinition> _definitions;

    public PublicErrorCatalog(IEnumerable<PublicErrorDefinition> definitions)
    {
        _definitions = definitions?.ToList().AsReadOnly()
            ?? throw new ArgumentNullException(nameof(definitions));
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