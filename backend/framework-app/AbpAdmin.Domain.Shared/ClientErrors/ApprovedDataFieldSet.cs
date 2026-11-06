using System;
using System.Collections.Generic;
using System.Linq;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 批准数据字段集合（方案 4.3 + P0 审查项）。P1 契约。
/// 实现出口批准字段的不可变集合，保证未批准字段无法进入投影结果。
/// </summary>
/// <remarks>
/// <para>
/// <b>审查项 约束</b>：
/// FriendlyBusinessExceptionMiddleware（约 88-97 行）把 business.Data 原样透传给客户端，
/// 没有白名单。P1-HTTP 出口必须使用本类型强制仅输出批准字段。
/// </para>
/// <para>
/// <b>设计原则</b>：
/// 投影结果 <see cref="PublicErrorProjectionResult.Data"/> 只能通过 <see cref="Filter"/>
/// 方法构建；未批准键自动丢弃（或抛出，视调用点策略）。
/// 大小写规则：<see cref="ClientErrorDataKeys.Comparer"/>（Ordinal 大小写敏感），
/// 避免 <c>CorrelationId</c> vs <c>correlationId</c> 等逃脱检查。
/// </para>
/// <para>
/// <b>消费者</b>：P1 Domain 投影核心（构建 data）、P1-HTTP 出口 writer（二次校验）、
/// P1-HTTP filter（将定义中 <see cref="PublicErrorParameterDefinition"/> 的可回传字段转为批准集合）。
/// </para>
/// </remarks>
public sealed class ApprovedDataFieldSet
{
    private readonly HashSet<string> _approvedFields;

    /// <summary>
    /// 空集合（无任何批准字段）。
    /// </summary>
    public static ApprovedDataFieldSet Empty { get; } = new([]);

    /// <summary>
    /// 构造批准字段集合（使用 <see cref="ClientErrorDataKeys.Comparer"/> 大小写敏感比较）。
    /// 重复键被合并。
    /// </summary>
    public ApprovedDataFieldSet(IEnumerable<string> approvedFields)
    {
        if (approvedFields is null)
        {
            throw new ArgumentNullException(nameof(approvedFields));
        }

        _approvedFields = new HashSet<string>(
            approvedFields.Where(f => !string.IsNullOrEmpty(f)),
            ClientErrorDataKeys.Comparer);
        Fields = _approvedFields.OrderBy(f => f, ClientErrorDataKeys.Comparer).ToList();
    }

    /// <summary>
    /// 批准字段列表（只读快照，按 Ordinal 排序保证确定性输出）。
    /// </summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>
    /// 检查键是否批准（使用 <see cref="ClientErrorDataKeys.Comparer"/> 比较）。
    /// </summary>
    public bool IsApproved(string? key)
    {
        return !string.IsNullOrEmpty(key) && _approvedFields.Contains(key);
    }

    /// <summary>
    /// 过滤输入字典，仅保留批准键（返回新字典，不修改原字典）。
    /// null 输入返回空字典。
    /// 值不修改（原值原样保留，包括 null）。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Filter(IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(ClientErrorDataKeys.Comparer);
        foreach (var kvp in source)
        {
            if (IsApproved(kvp.Key))
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// 合并两个批准集合（返回新集合）。
    /// </summary>
    public ApprovedDataFieldSet Union(ApprovedDataFieldSet other)
    {
        if (other is null)
        {
            throw new ArgumentNullException(nameof(other));
        }
        return new ApprovedDataFieldSet(_approvedFields.Concat(other._approvedFields));
    }
}
