using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// HTTP 错误投影上下文（方案 5.3.1）。P1 契约。
/// 包含 HTTP 请求/响应状态和异常信息，供 <see cref="IClientErrorHttpProjector"/> 使用。
/// </summary>
public sealed class ClientErrorHttpProjectionContext
{
    /// <summary>
    /// 当前 HTTP 上下文（包含请求、响应、用户、路由等）。
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// 未捕获异常（已知时）。null 表示非异常场景（如授权拒绝、模型绑定错误）。
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// 端点登记（已知时）。null 表示未登记端点（走 Public 策略）。
    /// </summary>
    public AdminErrorEndpointRegistration? EndpointRegistration { get; init; }

    /// <summary>
    /// 有效受众（由 P1-HTTP 的 <see cref="IAdminErrorEligibilityResolver"/> 决定）。
    /// 默认 Public（安全降级）。
    /// </summary>
    public ClientErrorAudience EffectiveAudience { get; init; } = ClientErrorAudience.Public;

    /// <summary>
    /// 响应是否已开始。已开始的响应不能修改状态/追加 JSON。
    /// </summary>
    public bool HasResponseStarted => HttpContext.Response.HasStarted;
}

/// <summary>
/// HTTP 错误投影结果（方案 5.3.1）。P1 契约。
/// 包含投影后的状态码、响应体和等待时间，供 <see cref="IClientErrorResponseWriter"/> 写出。
/// </summary>
public sealed class ClientErrorHttpProjection
{
    /// <summary>
    /// 投影结果（非 HTTP 核心输出）。
    /// </summary>
    public required PublicErrorProjectionResult Body { get; init; }

    /// <summary>
    /// HTTP 状态码。
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>
    /// 等待秒数（如有）。用于 Retry-After 头和 body.retryAfterSeconds。
    /// null 表示无等待时间。
    /// </summary>
    public int? RetryAfterSeconds { get; init; }
}

/// <summary>
/// HTTP 错误投影器接口（方案 5.3.1）。P1 契约。
/// 负责将异常/HTTP 上下文投影为安全响应模型。
/// </summary>
/// <remarks>
/// <para>
/// 消费者：P1-HTTP 的 ClientErrorExceptionFilter、ClientErrorAbpExceptionHandlingMiddleware、
/// 授权异常 handler、协议感知 API 兜底中间件。
/// </para>
/// <para>
/// 实现约束：
/// </para>
/// <list type="bullet">
///   <item><description>不得抛出异常（所有异常必须安全降级）。</description></item>
///   <item><description>不得修改 HttpContext（只读访问）。</description></item>
///   <item><description>调用 Domain 层 <see cref="IClientErrorProjectionCore"/> 完成核心投影。</description></item>
/// </list>
/// </remarks>
public interface IClientErrorHttpProjector
{
    /// <summary>
    /// 执行投影：根据上下文环境（受众、端点、异常）生成安全响应模型。
    /// 已开始响应的场景返回 null（由调用方决定日志/中止策略）。
    /// </summary>
    ClientErrorHttpProjection? Project(ClientErrorHttpProjectionContext context);
}

/// <summary>
/// HTTP 错误响应写入器接口（方案 5.3.1）。P1 契约。
/// 负责一次性写出安全响应（状态码、Retry-After 头、ABP 信封）。
/// </summary>
/// <remarks>
/// <para>
/// 消费者：P1-HTTP 的异常 filter/中间件（所有受控入口统一出口）。
/// </para>
/// <para>
/// 实现约束：
/// </para>
/// <list type="bullet">
///   <item><description>写出前必须 <c>Response.Clear()</c>（按方案 5.3.6）。</description></item>
///   <item><description>Clear 后必须重新写 Retry-After 头（不保留任意原头）。</description></item>
///   <item><description>已开始响应时必须 no-op + 日志（不追加 JSON、不截断文件算成功）。</description></item>
///   <item><description>Data 字典必须经过批准字段过滤（<see cref="ApprovedDataFieldSet"/>）。</description></item>
///   <item><description>禁止写出 details（客户端始终 null）。</description></item>
/// </list>
/// </remarks>
public interface IClientErrorResponseWriter
{
    /// <summary>
    /// 写出安全响应。若响应已开始，则 no-op 并记录日志。
    /// </summary>
    Task WriteAsync(HttpContext httpContext, ClientErrorHttpProjection projection, CancellationToken cancellationToken = default);
}
