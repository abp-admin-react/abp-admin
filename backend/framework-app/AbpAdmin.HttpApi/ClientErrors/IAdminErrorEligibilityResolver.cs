using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 管理资格解析结果（方案 4.2）。P1 契约。
/// </summary>
public sealed class AdminErrorEligibilityResult
{
    /// <summary>
    /// 是否具备 AdminCompatibility 资格。
    /// true 时走管理兼容规则；false 时降级 Public。
    /// </summary>
    public bool IsEligible { get; init; }

    /// <summary>
    /// 降级原因（非 Eligible 时必填）。
    /// 用于计数指标（不含敏感信息：token、用户 ID、输入原值）。
    /// </summary>
    public AdminEligibilityDegradeReason? DegradeReason { get; init; }

    /// <summary>
    /// 详细消息（可选，仅供日志；不写入响应）。
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// 管理资格降级原因（方案 4.2）。P1 契约。
/// </summary>
public enum AdminEligibilityDegradeReason
{
    /// <summary>未在登记表中找到端点。</summary>
    NotRegistered,

    /// <summary>端点未声明 AdminCompatibility。</summary>
    NotDeclaredAdminCompatibility,

    /// <summary>未认证（会话/租户未就绪）。</summary>
    Unauthenticated,

    /// <summary>会话或租户未就绪（认证中但未完成）。</summary>
    SessionOrTenantNotReady,

    /// <summary>权限检查失败。</summary>
    PermissionDenied,

    /// <summary>资格查询失败（异常、服务不可用等）。</summary>
    EligibilityQueryFailed,
}

/// <summary>
/// 管理资格解析上下文（方案 4.2）。P1 契约。
/// </summary>
public sealed class AdminErrorEligibilityContext
{
    /// <summary>
    /// 当前 HTTP 上下文。
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// 端点登记（已知时）。null 表示未登记。
    /// </summary>
    public AdminErrorEndpointRegistration? EndpointRegistration { get; init; }
}

/// <summary>
/// 管理资格解析器接口（方案 4.2）。P1 契约。
/// 实现方案 4.2 的资格判定顺序。
/// </summary>
/// <remarks>
/// <para>
/// <b>资格判定顺序</b>（严格按此执行）：
/// </para>
/// <list type="number">
///   <item><description>必须是登记表中的具体端点/服务方法，且声明 AdminCompatibility。</description></item>
///   <item><description>会话、租户、认证状态已通过校验。</description></item>
///   <item><description>已有的管理功能资格权限通过（不新增全局权限）。</description></item>
///   <item><description>原类级、方法级、资源级授权全部继续执行。</description></item>
///   <item><description>请求本身授权失败时不得升级错误输出。</description></item>
///   <item><description>SettingUi 等代理权限必须显式登记。</description></item>
///   <item><description>资格无法确定、查询失败、会话/租户未就绪时均降级 Public。</description></item>
/// </list>
/// <para>
/// <b>安全规则</b>：
/// </para>
/// <list type="bullet">
///   <item><description>clientType、请求头、Origin、User-Agent、URL 前缀、公开 OAuth client_id 不能授予管理资格。</description></item>
///   <item><description>仅 [Authorize] 的个人资料、我的通知等登记 Public/Shared，不把"已登录"等同管理者。</description></item>
///   <item><description>不记录 token、输入原值、原始异常文本；不使用用户 ID 作为无界指标标签。</description></item>
/// </list>
/// <para>
/// 消费者：P1-HTTP 的异常 filter/中间件（决定错误输出策略）。
/// </para>
/// </remarks>
public interface IAdminErrorEligibilityResolver
{
    /// <summary>
    /// 解析当前请求是否具备 AdminCompatibility 资格。
    /// </summary>
    AdminErrorEligibilityResult Resolve(AdminErrorEligibilityContext context);
}
