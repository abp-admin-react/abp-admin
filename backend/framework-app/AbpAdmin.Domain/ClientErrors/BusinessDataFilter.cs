using System;
using System.Collections.Generic;
using AbpAdmin.ClientErrors;

namespace AbpAdmin.Domain.ClientErrors;

/// <summary>
/// 业务异常数据过滤器 - 用于安全过滤业务异常中的 data 字段。
/// 实现 P0 审查项 (a)：批准数据字段白名单。
/// </summary>
public static class BusinessDataFilter
{
    private static readonly ApprovedDataFieldSet DefaultApprovedFields = new(
        new[] { ClientErrorFieldNames.CorrelationId });

    /// <summary>
    /// 使用默认批准字段过滤业务数据。
    /// 默认只允许 correlationId。
    /// </summary>
    public static IReadOnlyDictionary<string, object?> FilterDefault(
        IReadOnlyDictionary<string, object?>? source)
    {
        return DefaultApprovedFields.Filter(source);
    }

    /// <summary>
    /// 使用指定的批准字段集合过滤业务数据。
    /// </summary>
    public static IReadOnlyDictionary<string, object?> FilterWithApprovedFields(
        IReadOnlyDictionary<string, object?>? source,
        ApprovedDataFieldSet approvedFields)
    {
        if (approvedFields == null)
        {
            throw new ArgumentNullException(nameof(approvedFields));
        }

        return approvedFields.Filter(source);
    }

    /// <summary>
    /// 使用端点登记的批准字段过滤业务数据。
    /// </summary>
    public static IReadOnlyDictionary<string, object?> FilterWithEndpointRegistration(
        IReadOnlyDictionary<string, object?>? source,
        AdminErrorEndpointRegistration? registration)
    {
        if (registration == null || registration.ApprovedDataFields == null
            || registration.ApprovedDataFields.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var approvedFields = new ApprovedDataFieldSet(registration.ApprovedDataFields);
        return approvedFields.Filter(source);
    }
}