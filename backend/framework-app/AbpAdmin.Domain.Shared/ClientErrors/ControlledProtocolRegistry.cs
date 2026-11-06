using System.Collections.Generic;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 受控协议登记项（方案 5.3.3）。P1 契约。
/// 用于启动时建立受控入口到错误协议的映射（ABP/OAuth/Gateway/SSE 等）。
/// </summary>
/// <remarks>
/// <para>
/// 每个受控 HTTP 入口（MVC 端点/中间件/OAuth/connect/SignalR 握手）都需要登记协议，
/// 以便未捕获异常时按协议规范写出安全错误响应（不套错 ABP 信封）。
/// </para>
/// <para>
/// <b>登记策略</b>：
/// </para>
/// <list type="bullet">
///   <item><description>精确路由优先（如 /connect/token → OAuth）。</description></item>
///   <item><description>路径段前缀次之（如 /api → ABP）。</description></item>
///   <item><description>未登记的 /api 仍按 ABP 公开兜底。</description></item>
///   <item><description>非 API（Razor 页面）返回 null（走原异常页）。</description></item>
/// </list>
/// </remarks>
public sealed record ControlledProtocolRegistration
{
    /// <summary>
    /// 端点唯一标识（稳定，格式：{类型全名}.{方法名}({参数签名})）。
    /// 与 <see cref="AdminErrorEndpointRegistration.ExitId"/> 一致。
    /// </summary>
    public required string ExitId { get; init; }

    /// <summary>
    /// 错误协议（ABP/OAuth/Gateway/SSE 等）。
    /// </summary>
    public required ErrorProtocol Protocol { get; init; }

    /// <summary>
    /// HTTP 方法（如 POST）。null 表示所有方法匹配。
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// 路由前缀或完整路由模板（如 "connect/token"、"api"、"v1"）。
    /// 匹配规则：精确匹配 > 前缀匹配 > 默认 /api 兜底。
    /// </summary>
    public string? RoutePrefix { get; init; }

    /// <summary>
    /// 生产者类型全名（用于非 MVC 出口定位，如中间件类型名）。
    /// null 表示 MVC 端点通过 ExitId 定位。
    /// </summary>
    public string? ProducerType { get; init; }

    /// <summary>
    /// 备注（协议适配说明、异常处理分支等）。
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// 受控协议注册表接口（方案 5.3.3）。P1 契约。
/// </summary>
/// <remarks>
/// <para>
/// 启动时由 Host 层扫描并构建；P1-HTTP 出口投影使用 <see cref="FindMatch"/>
/// 确定 HTTP 异常应使用的错误协议。
/// </para>
/// <para>
/// 实现约束：必须是单例；启动完成后冻结；并发读安全。
/// </para>
/// </remarks>
public interface IControlledProtocolRegistry
{
    /// <summary>
    /// 默认协议（ABP 信封）。未登记的 /api 入口使用此协议。
    /// 非 API（Razor）返回 null。
    /// </summary>
    ErrorProtocol? DefaultProtocol => ErrorProtocol.AbpEnvelope;

    /// <summary>
    /// 全部登记项（只读视图）。
    /// </summary>
    IReadOnlyCollection<ControlledProtocolRegistration> GetAll();

    /// <summary>
    /// 根据 HTTP 方法和路径查找协议登记项。
    /// 返回 null 表示非受控入口（如 Razor 页面），应走原异常页。
    /// </summary>
    /// <param name="httpMethod">HTTP 方法（GET/POST 等），大小写不敏感。</param>
    /// <param name="path">请求路径（如 /connect/token），已去除查询字符串."</param>
    ControlledProtocolRegistration? FindMatch(string? httpMethod, string path);
}

/// <summary>
/// 受控协议路径前缀常量（方案 5.3.3 + 方案 5.1 目录与职责）。P1 契约。
/// </summary>
public static class ControlledProtocolPathPrefixes
{
    /// <summary>
    /// ABP 动态 API 默认前缀。
    /// 未登记的 /api 入口默认使用 <see cref="ErrorProtocol.AbpEnvelope"/>。
    /// </summary>
    public const string Api = "api";

    /// <summary>
    /// 客户公开 API 前缀（方案 8 客户端点路由）。
    /// <c>/api/client/...</c> 走客户公开规则。
    /// </summary>
    public const string ClientApi = "api/client";

    /// <summary>
    /// OAuth/OpenIddict 端点前缀（/connect/token、/connect/authorize 等）。
    /// 使用 <see cref="ErrorProtocol.OAuth"/> 错误格式（{error, error_description} + WWW-Authenticate）。
    /// </summary>
    public const string Connect = "connect";

    /// <summary>
    /// TokenGateway /v1 路由前缀。
    /// 使用 <see cref="ErrorProtocol.Gateway"/> 独立协议（GatewayError 形状）。
    /// </summary>
    public const string TokenGatewayV1 = "v1";

    /// <summary>
    /// SignalR Hub 端点前缀。
    /// 握手错误按握手协议处理，非 Hub 数据通道错误。
    /// </summary>
    public const string SignalRHubs = "signalr-hubs";
}
