using System;
using System.Collections.Generic;
using Volo.Abp;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 启动期公开目录校验：重复内部码、基础码 kind、同一公开码不一致定义。
/// 只读内存，不访问数据库或外部服务。
/// </summary>
public static class PublicErrorCatalogStartupValidator
{
    public static void Validate(IEnumerable<PublicErrorDefinition> definitions)
    {
        if (definitions is null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        var byInternal = new HashSet<string>(StringComparer.Ordinal);
        var byPublic = new Dictionary<string, PublicErrorDefinition>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (string.IsNullOrEmpty(definition.InternalCode) || string.IsNullOrEmpty(definition.PublicCode))
            {
                throw new AbpInitializationException("Public error definition requires InternalCode and PublicCode.");
            }

            if (!byInternal.Add(definition.InternalCode))
            {
                throw new AbpInitializationException(
                    $"Duplicate internal error code '{definition.InternalCode}'.");
            }

            if (ClientErrorKindMapping.TryGetKind(definition.PublicCode, out var expectedKind)
                && definition.Kind != expectedKind)
            {
                throw new AbpInitializationException(
                    $"Base public code '{definition.PublicCode}' kind must be {expectedKind}, got {definition.Kind}.");
            }

            if (definition.InternalCode == AbpAdminDomainErrorCodes.Imaging.ImageProcessingBusy
                && definition.Kind == PublicErrorKind.RateLimited)
            {
                throw new AbpInitializationException(
                    "AbpAdmin:ImageProcessingBusy must map to Client:Unexpected/server, not rateLimited.");
            }

            if (byPublic.TryGetValue(definition.PublicCode, out var existing)
                && !AreConsistent(existing, definition))
            {
                throw new AbpInitializationException(
                    $"Inconsistent public error definition for '{definition.PublicCode}'.");
            }

            byPublic[definition.PublicCode] = definition;
        }
    }

    public static bool AreConsistent(PublicErrorDefinition left, PublicErrorDefinition right)
    {
        return left.Kind == right.Kind
               && string.Equals(left.MessageKey, right.MessageKey, StringComparison.Ordinal)
               && left.ResourceType == right.ResourceType;
    }
}
