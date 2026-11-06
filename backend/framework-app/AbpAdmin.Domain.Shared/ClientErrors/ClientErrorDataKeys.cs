using System;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 客户端错误数据键比较规则（方案 P0 审查项+ 方案 3.1 客户端 lowerCamelCase）。
/// P1 契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>审查项 约束</b>：
/// ClientErrorCodes 字段名大小写敏感：契约明确字段名（data 键，如 correlationId/retryAfterSeconds
/// 与管理端兼容的 RetryAfterSeconds）的大小写规则与比较方式。
/// </para>
/// <para>
/// <b>大小写规则</b>：
/// </para>
/// <list type="bullet">
///   <item><description>客户端响应字段统一 lowerCamelCase（<see cref="ClientErrorFieldNames"/>）。</description></item>
///   <item><description>管理端兼容字段保持历史 PascalCase（如 RetryAfterSeconds）。</description></item>
///   <item><description>比较必须使用 <see cref="StringComparer.Ordinal"/>（大小写敏感）。</description></item>
/// </list>
/// <para>
/// <b>为什么 Ordinal（大小写敏感）</b>：
/// </para>
/// <list type="bullet">
///   <item><description>JSON 成员名大小写敏感（RFC 8259 § 5；System.Text.Json 默认区分大小写）。</description></item>
///   <item><description>大小写敏感比较防止 <c>CorrelationId</c> vs <c>correlationId</c> 同时出现时逃脱检查。</description></item>
///   <item><description>与其他安全机制一致：幂等键、缓存键、审计关联号均区分大小写。</description></item>
///   <item><description>快照导出和契约匹配确定性：避免因大小写归一化导致前后端理解不一致。</description></item>
/// </list>
/// <para>
/// <b>管理端兼容</b>：
/// 管理端历史字段（如 RetryAfterSeconds）必须以其注册时的精确大小写登记在
/// <see cref="AdminErrorEndpointRegistration.ApprovedDataFields"/> 中；
/// 投影时按注册精确大小写写回，不做大小写转换。
/// 客户端 lowerCamelCase 字段独立登记，与管理端字段并存于批准集合时互不影响（键不同）。
/// </para>
/// </remarks>
public static class ClientErrorDataKeys
{
    /// <summary>
    /// 数据键比较器（Ordinal 大小写敏感）。
    /// 用于所有 data 字典键比较：批准字段检查、过滤、合并、快照生成。
    /// </summary>
    public static StringComparer Comparer => StringComparer.Ordinal;

    /// <summary>
    /// 判断两个键是否相等（Ordinal 大小写敏感）。
    /// 空值处理：<c>null</c> 只等于 <c>null</c>（与 <see cref="string.Equals(string, string, StringComparison)"/> 一致）。
    /// </summary>
    public static bool KeysEqual(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }
}
