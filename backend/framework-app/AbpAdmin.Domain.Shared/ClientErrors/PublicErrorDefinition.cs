using System;
using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 公开错误定义（方案 5.2）。P1 契约，模块通过 <see cref="IPublicErrorDefinitionProvider"/> 注册。
/// </summary>
/// <remarks>
/// <para>
/// 该结构是新契约设计，不是现成 ABP API。示例：内部 <c>BizTemplate:ProjectUnavailable</c>
/// 映射到 <c>BizTemplate:Public:ProjectUnavailable</c>，无任何异常原文参数。
/// </para>
/// <para>
/// 多个内部码可收敛到同一公开定义；同一内部键重复登记或同一公开码出现不一致定义
/// 必须在启动阶段失败（由 P1 Domain 的 PublicErrorDefinitionRegistry 强制）。
/// </para>
/// <para>
/// 本记录只承载声明；它自身不执行本地化、不读取 HttpContext、不触碰 Exception.Data，
/// 保证契约层可以在启动校验、快照导出和前端契约生成中安全序列化。
/// </para>
/// </remarks>
public sealed record PublicErrorDefinition
{
    /// <summary>
    /// 内部查找键，仅服务端保存，绝不写入客户响应或前端快照。
    /// </summary>
    public required string InternalCode { get; init; }

    /// <summary>
    /// 公开稳定码（如 <c>BizTemplate:Public:ProjectUnavailable</c>）。
    /// 用于页面流程判断；允许多个内部码映射到同一公开码。
    /// </summary>
    public required string PublicCode { get; init; }

    /// <summary>
    /// 静态客户资源类型（必须实现 <see cref="IClientStaticResource"/> 标记，
    /// 且不接受数据库动态覆盖，见方案 6.2）。
    /// </summary>
    public required Type ResourceType { get; init; }

    /// <summary>
    /// 客户文案键（在 <see cref="ResourceType"/> 对应的静态客户资源内）。
    /// </summary>
    public required string MessageKey { get; init; }

    /// <summary>
    /// 允许参与插值/回传的参数定义。空列表表示该错误没有任何参数。
    /// 模板插值前先按此筛选，不能先用全部 Exception.Data 生成文案（方案 3.4）。
    /// </summary>
    public IReadOnlyList<PublicErrorParameterDefinition> AllowedParameters { get; init; } = [];

    /// <summary>
    /// 公开 DTO 字段与校验规则映射（方案 3.4）。
    /// 不凭消息正则反推规则；未知规则由投影核心映射为通用校验提示。
    /// </summary>
    public IReadOnlyList<PublicErrorValidationRule> ValidationRules { get; init; } = [];

    /// <summary>
    /// 允许导出给客户端的分类（方案 5.2 PublicKind）。
    /// 基础码的 kind 固定由 <see cref="ClientErrorKindMapping"/> 决定，模块不可重新定义。
    /// </summary>
    public required PublicErrorKind Kind { get; init; }
}

/// <summary>
/// 公开错误参数定义（方案 3.4：必须包含来源、类型、长度/范围）。
/// </summary>
/// <remarks>
/// 禁止任意对象 <c>ToString()</c>；禁止把 <c>ex.Message</c> 放进名为 reason 的允许字段。
/// 参数缺失或类型非法时由投影核心使用无参数安全兜底，不抛出第二个格式化异常。
/// </remarks>
public sealed record PublicErrorParameterDefinition
{
    /// <summary>
    /// 公开参数名（客户端 lowerCamelCase，如 <c>retryAfterSeconds</c>）。
    /// 同时是回传到客户端 data 字典中的键（当 <see cref="CanReturnToClient"/> 为 true）。
    /// 键比较规则见 <see cref="ClientErrorDataKeys"/>（Ordinal 大小写敏感）。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 参数来源。
    /// </summary>
    public required PublicErrorParameterSource Source { get; init; }

    /// <summary>
    /// 来源键：<see cref="Source"/> 为 <see cref="PublicErrorParameterSource.ExceptionData"/> 时
    /// 指定 Exception.Data 中受控读取的键；其余来源必须为 null。
    /// </summary>
    public string? SourceKey { get; init; }

    /// <summary>
    /// 参数值类型。投影核心按此做类型校验，非法类型走安全兜底。
    /// </summary>
    public required PublicErrorParameterValueType ValueType { get; init; }

    /// <summary>
    /// 字符串值最大长度（仅 <see cref="PublicErrorParameterValueType.String"/> 有效）。
    /// 不得超过 <see cref="ClientErrorHandlingLimits.AbsoluteMaxPublicParameterLength"/>。
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// 数值下限（仅数值类型有效）。
    /// </summary>
    public long? MinValue { get; init; }

    /// <summary>
    /// 数值上限（仅数值类型有效）。
    /// </summary>
    public long? MaxValue { get; init; }

    /// <summary>
    /// 是否可回传到客户端 data 字典。false 表示仅允许参与服务端文案插值。
    /// </summary>
    public bool CanReturnToClient { get; init; }
}

/// <summary>
/// 公开错误参数来源。
/// </summary>
public enum PublicErrorParameterSource
{
    /// <summary>
    /// 来自 Exception.Data 中显式登记的受控键（禁止整体透传 Data）。
    /// </summary>
    ExceptionData,

    /// <summary>
    /// 框架统一关联号（canonical correlation id，见方案 3.1 关联编号节）。
    /// </summary>
    CorrelationId,

    /// <summary>
    /// 经 <see cref="RetryAfterResolver"/> 计算的单一安全等待秒数。
    /// </summary>
    RetryAfterSeconds,
}

/// <summary>
/// 公开错误参数值类型。
/// </summary>
public enum PublicErrorParameterValueType
{
    /// <summary>字符串（受 <c>MaxLength</c> 约束）。</summary>
    String,

    /// <summary>32 位整数（受 Min/Max 约束）。</summary>
    Int32,

    /// <summary>64 位整数（受 Min/Max 约束）。</summary>
    Int64,

    /// <summary>布尔值。</summary>
    Boolean,
}

/// <summary>
/// 公开校验规则（方案 3.4：客户 DTO 字段 + 已知规则 → 客户资源）。
/// </summary>
public sealed record PublicErrorValidationRule
{
    /// <summary>
    /// 客户 DTO 的公开字段名（lowerCamelCase）。
    /// <c>validationErrors[].members</c> 只允许出现这里登记的字段。
    /// </summary>
    public required string PublicFieldName { get; init; }

    /// <summary>
    /// 校验规则种类。未知规则不进入本表，由投影核心统一给通用校验提示。
    /// </summary>
    public required PublicValidationRuleKind RuleKind { get; init; }

    /// <summary>
    /// 客户资源中的校验文案键。
    /// </summary>
    public required string MessageKey { get; init; }
}

/// <summary>
/// 公开校验规则种类。
/// </summary>
public enum PublicValidationRuleKind
{
    /// <summary>必填。</summary>
    Required,

    /// <summary>字符串长度。</summary>
    StringLength,

    /// <summary>数值范围。</summary>
    Range,

    /// <summary>格式（正则/邮箱/URL 等）。</summary>
    Format,
}
