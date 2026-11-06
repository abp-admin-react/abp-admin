using System;
using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 客户端 HTTP 200 任务/批量失败结果契约（方案 3.3）。
/// 用于异步任务、批量操作、导出状态等 HTTP 200 响应中的失败信息。
/// </summary>
/// <remarks>
/// <para>
/// 即使 HTTP 200，也不能原样返回内部 <c>ErrorMessage</c>。
/// 本 DTO 只包含 Code、Message、CorrelationId 和确需的公开字段结果；
/// 业务 DTO 映射时调用 Domain 层非 HTTP 投影核心生成。
/// </para>
/// <para>
/// <b>字段说明</b>：
/// </para>
/// <list type="bullet">
///   <item><description>Code、Message 必填（投影核心保证）。</description></item>
///   <item><description>CorrelationId 可空（历史数据可能无关联号，不伪造）。</description></item>
///   <item><description>Data 仅包含批准字段，由投影核心通过 <see cref="ApprovedDataFieldSet"/> 构建。</description></item>
///   <item><description>允许部分成功（保留原逐项结构，由业务 DTO 决定）。</description></item>
/// </list>
/// <para>
/// <b>无码字符串处理</b>：只有失败字符串、没有结构化内部码时，一律映射
/// <see cref="ClientErrorCodes.OperationFailed"/> + 该码的静态通用文案，不分析字符串反推业务原因。
/// 原串只留受权日志。
/// </para>
/// <para>
/// <b>使用方式</b>：
/// 业务 DTO 的失败字段需要显式接入本 DTO；既有管理监控成功 DTO 不在本轮重写范围。
/// 后台作业沿用其执行时关联号；历史结果缺关联号时可保持空。
/// </para>
/// <para>
/// <b>序列化约定</b>：ABP JSON 序列化使用 camelCase，因此前端 TS 镜像使用 lowerCamelCase。
/// Data 字典的键必须与批准字段一致（<see cref="ClientErrorDataKeys.Comparer"/> Ordinal 比较）。
/// </para>
/// </remarks>
public class ClientFailureDto
{
    /// <summary>
    /// 公开稳定码（必填）。默认 <see cref="ClientErrorCodes.OperationFailed"/>。
    /// 投影核心保证非空；无码字符串场景使用 OperationFailed。
    /// </summary>
    public string Code { get; set; } = ClientErrorCodes.OperationFailed;

    /// <summary>
    /// 安全文案（必填）。来自静态客户资源或通用兜底。
    /// 投影核心保证非空；绝不包含内部异常原文、SQL、路径等敏感信息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 关联号（可空）。来自请求时的 canonical correlation id。
    /// 历史数据可能为空（不伪造无法关联到历史日志的新号）。
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// 公开字段结果（仅包含批准字段）。
    /// 空字典表示无批准字段；调用方无需判空。
    /// 键使用客户端 lowerCamelCase，与管理端兼容字段并存时区分大小写。
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();
}
