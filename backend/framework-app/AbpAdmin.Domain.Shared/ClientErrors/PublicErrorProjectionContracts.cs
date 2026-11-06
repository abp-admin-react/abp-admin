using System;
using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 非 HTTP 投影核心输入契约（方案 5.2）。
/// 无 HttpContext 依赖，可被 Domain 投影核心、Application（ClientFailureDto 映射）和测试安全调用。
/// </summary>
/// <remarks>
/// <para>
/// 消费者：Domain 层 <see cref="IClientErrorProjectionCore"/> 实现、
/// Application 层的 ClientFailureDto 映射、HTTP 层的 <see cref="IClientErrorHttpProjector"/>（组装输入）。
/// </para>
/// </remarks>
public sealed record PublicErrorProjectionInput
{
    /// <summary>
    /// 内部错误码（来自异常或显式设置）。null 表示无码场景（走安全兜底）。
    /// </summary>
    public string? InternalCode { get; init; }

    /// <summary>
    /// Exception.Data 的只读快照（由调用方提取，不传递异常对象本身）。
    /// 投影核心仅读取按定义批准的键（见 <see cref="ApprovedDataFieldSet"/>）。
    /// null 或空字典表示无参数。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExceptionData { get; init; }

    /// <summary>
    /// HTTP 状态码（由调用方根据异常类型或 <see cref="IHttpExceptionStatusCodeFinder"/> 确定）。
    /// 用于出口组合校验（<see cref="PublicErrorExitCombination"/>）。
    /// 投影核心本身不强制状态码映射，交由 HTTP 层处理。
    /// </summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>
    /// 框架统一关联号（canonical correlation id，方案 3.1）。
    /// null 表示无关联号（旧数据或未启用）。
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// 有效受众（由 HTTP 层根据端点登记和资格解析确定）。
    /// 用于选择投影策略（Public/Shared/AdminCompatibility）。
    /// </summary>
    public ClientErrorAudience Audience { get; init; } = ClientErrorAudience.Public;

    /// <summary>
    /// 经 <see cref="RetryAfterResolver"/> 计算的单一安全等待秒数。
    /// null 表示无等待时间。
    /// </summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// 目标文化名（如 zh-Hans）。null 使用默认文化。
    /// </summary>
    public string? CultureName { get; init; }

    /// <summary>
    /// 目标端点登记（已知时）。null 表示未知端点（走 Public 策略）。
    /// </summary>
    public AdminErrorEndpointRegistration? EndpointRegistration { get; init; }
}

/// <summary>
/// 投影后的校验错误项（方案 3.1 结构）。
/// </summary>
public sealed record PublicValidationErrorItem
{
    /// <summary>
    /// 安全文案（来自公开校验规则对应的客户资源，不拼接框架原文）。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 公开 DTO 字段名列表（lowerCamelCase）。
    /// <c>members</c> 只允许出现 <see cref="PublicErrorDefinition.ValidationRules"/> 中登记的字段。
    /// </summary>
    public required IReadOnlyList<string> Members { get; init; }
}

/// <summary>
/// 非 HTTP 投影核心输出契约（方案 5.2）。
/// </summary>
/// <remarks>
/// <para>
/// 包含投影后的所有字段值：公开码、分类、已本地化的安全文案、经批准过滤的 data、校验错误、等待秒数。
/// HTTP 层直接使用此结果组装响应；Application 层用于 ClientFailureDto 映射。
/// </para>
/// <para>
/// <b>关键安全保证</b>：<see cref="Data"/> 只包含批准字段，由投影核心使用
/// <see cref="ApprovedDataFieldSet"/> 构造，调用方无法绕过。
/// </para>
/// </remarks>
public sealed record PublicErrorProjectionResult
{
    /// <summary>
    /// 公开稳定码。null 表示无公开码（如 401 的无码协议场景）。
    /// </summary>
    public string? PublicCode { get; init; }

    /// <summary>
    /// 公开分类（与 <see cref="PublicCode"/> 映射一致）。
    /// </summary>
    public required PublicErrorKind Kind { get; init; }

    /// <summary>
    /// 已本地化的安全文案（绝不包含内部异常原文、SQL、路径等敏感信息）。
    /// 当资源缺失时使用该公开码的通用兜底文案。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 经批准字段过滤后的 data 字典（只读）。
    /// 空字典表示无批准字段；不回传任何数据。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// 校验错误列表（可能为空）。
    /// </summary>
    public IReadOnlyList<PublicValidationErrorItem> ValidationErrors { get; init; } = [];

    /// <summary>
    /// 等待秒数（来自 <see cref="RetryAfterResolver"/> 的最终结果）。
    /// null 表示无等待时间。
    /// </summary>
    public int? RetryAfterSeconds { get; init; }

    /// <summary>
    /// 关联号（来自输入，原样透传）。
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 非 HTTP 投影核心接口（方案 5.2）。
/// 定义在 Domain.Shared，实现由 Domain 层提供，消费者通过 DI 获取。
/// </summary>
/// <remarks>
/// <para>
/// 为什么要接口：Domain.Shared 是契约层，定义输入/输出类型与核心投影抽象，
/// 使 Application 层（ClientFailureDto 映射）和 HTTP 层都能依赖契约而非实现。
/// 实现逻辑（目录查找、参数筛选、资源本地化）留在 Domain。
/// </para>
/// <para>
/// 实现约束：
/// </para>
/// <list type="bullet">
///   <item><description>绝不应抛出格式化异常或二次异常；任何输入问题都应安全降级。</description></item>
///   <item><description>不得修改输入参数，不得缓存（每次投影独立）。</description></item>
///   <item><description>本地化文案必须从静态客户资源获取，不依赖数据库。</description></item>
/// </list>
/// </remarks>
public interface IClientErrorProjectionCore
{
    /// <summary>
    /// 执行投影：根据输入查找定义、筛选参数、本地化文案、构建批准 data 字典。
    /// 输入中未提供的信息（如无 InternalCode）走安全兜底逻辑。
    /// </summary>
    PublicErrorProjectionResult Project(PublicErrorProjectionInput input);
}
