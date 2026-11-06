using System;
using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 客户端错误处理配置节（方案 5.4）。
/// </summary>
/// <remarks>
/// <para>
/// 配置节名称：<see cref="SectionName"/> = "ClientErrorHandling"。
/// Host 使用严格绑定（.NET 10 ErrorOnUnknownConfiguration 或等价验证），拒绝未知/宽松键。
/// </para>
/// <para>
/// <b>安全默认值</b>：所有字段默认安全，无 ShowAllErrors/ExposeDetails/AllowRawMessage 等放开原文选项。
/// </para>
/// <para>
/// <b>验证</b>：Domain 层的 PublicErrorOptionsValidator 负责文化、类型和范围校验；
/// 前端镜像一致限制（<see cref="CustomerErrorLimits"/>）。
/// </para>
/// </remarks>
public class ClientErrorHandlingOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "ClientErrorHandling";

    /// <summary>
    /// 回退文化（null 表示跟随应用默认文化）。
    /// 显式值必须属于 <see cref="ClientErrorCultures.Supported"/> 列表。
    /// </summary>
    public string? FallbackCulture { get; set; } = null;

    /// <summary>
    /// 是否在响应 body 中包含关联号（client-facing correlationId 字段）。
    /// 不影响响应头（由 AbpCorrelationIdOptions.SetResponseHeader 控制）。
    /// </summary>
    public bool IncludeCorrelationId { get; set; } = true;

    /// <summary>
    /// 公开消息最大长度（防御性限制，防止超长文案泄露敏感片段）。
    /// 超限使用安全兜底文案。默认 1000 字符。
    /// 必须为正数且不得超过 <see cref="ClientErrorHandlingLimits.AbsoluteMaxPublicMessageLength"/>。
    /// </summary>
    public int MaxPublicMessageLength { get; set; } = 1000;

    /// <summary>
    /// 校验错误最大数量（防止泛洪客户端）。
    /// 超限截断并使用通用校验提示。默认 20 条。
    /// 必须为正数且不得超过 <see cref="ClientErrorHandlingLimits.AbsoluteMaxValidationErrors"/>。
    /// </summary>
    public int MaxValidationErrors { get; set; } = 20;

    /// <summary>
    /// 公开参数值最大长度（字符串参数截断上限）。
    /// 超限使用安全兜底文案。默认 256 字符。
    /// 必须为正数且不得超过 <see cref="ClientErrorHandlingLimits.AbsoluteMaxPublicParameterLength"/>。
    /// </summary>
    public int MaxPublicParameterLength { get; set; } = 256;
}

/// <summary>
/// 客户端错误处理绝对限制（框架上限，方案 5.4）。
/// </summary>
/// <remarks>
/// <para>
/// 这些值是框架允许的绝对上限，由 Domain 层 PublicErrorOptionsValidator 强制，
/// 用于防止配置值过大导致资源耗尽或安全隐患。
/// </para>
/// <para>
/// 前端镜像一致限制：<see cref="CustomerErrorLimits"/>（web/src/abp/customer/errors.ts）。
/// </para>
/// </remarks>
public static class ClientErrorHandlingLimits
{
    /// <summary>
    /// 公开消息绝对最大长度（4000 字符，约为默认值的 4 倍）。
    /// </summary>
    public const int AbsoluteMaxPublicMessageLength = 4000;

    /// <summary>
    /// 校验错误绝对最大数量（100 条，约为默认值的 5 倍）。
    /// </summary>
    public const int AbsoluteMaxValidationErrors = 100;

    /// <summary>
    /// 公开参数绝对最大长度（1024 字符，约为默认值的 4 倍）。
    /// </summary>
    public const int AbsoluteMaxPublicParameterLength = 1024;
}

/// <summary>
/// 客户端错误静态资源支持文化（方案 6.1）。
/// </summary>
/// <remarks>
/// <para>
/// 首期静态客户支持集固定为 zh-Hans、zh-Hant、en。
/// 这是本期明确支持集，不是对现有全部前端/后端文化数量的断言。
/// </para>
/// <para>
/// 完整性/占位参数/默认文化校验只作用于登记并标记 <see cref="IClientStaticResource"/>
/// 的静态客户资源，不倒查所有历史管理资源；未支持语言回退客户默认文化。
/// </para>
/// <para>
/// 未来扩充客户支持集时必须补齐所有客户资源，CI 检查。
/// </para>
/// </remarks>
public static class ClientErrorCultures
{
    /// <summary>
    /// 支持的文化列表（zh-Hans/zh-Hant/en）。
    /// </summary>
    public static readonly IReadOnlyList<string> Supported = ["zh-Hans", "zh-Hant", "en"];
}

/// <summary>
/// 客户端静态资源标记接口（方案 6.2）。
/// </summary>
/// <remarks>
/// <para>
/// 客户静态资源类（如 <c>AbpAdminClientErrorsResource</c>）实现此接口，
/// 用于 Domain 模块跳过数据库贡献者、LanguageTextAppService 拒绝动态覆盖、
/// 契约快照验证资源标记。
/// </para>
/// <para>
/// 关键行为：
/// </para>
/// <list type="bullet">
///   <item><description><see cref="AbpAdminDomainModule"/> 配置语言贡献者时跳过实现此接口的资源类型。</description></item>
///   <item><description><see cref="Localization.LanguageTextAppService.UpdateAsync"/> 拒绝对实现此接口的资源新增/覆盖。</description></item>
///   <item><description>允许按既有权限清理历史覆盖（恢复默认）。</description></item>
///   <item><description>资源读取使用同一静态目录，不受数据库贡献者影响。</description></item>
/// </list>
/// </remarks>
public interface IClientStaticResource;
