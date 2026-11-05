using System;
using System.Collections.Generic;
using System.Linq;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 管理端错误端点登记项。
/// P0 阶段建立登记结构，不改变运行时行为。
/// </summary>
/// <remarks>
/// 登记键采用"具体控制器/服务类型全名 + 方法名 + 参数类型签名"，
/// 同时记录 HTTP method/route 解析结果，以区分重载。
/// 不以权限组前缀、命名或路径自动授予 AdminCompatibility。
/// </remarks>
public sealed record AdminErrorEndpointRegistration
{
    /// <summary>
    /// 稳定的端点唯一标识，格式：{类型全名}.{方法名}({参数签名})。
    /// </summary>
    public required string ExitId { get; init; }

    /// <summary>
    /// 声明该端点的类型全名（控制器或 AppService）。
    /// </summary>
    public required string DeclaringType { get; init; }

    /// <summary>
    /// 方法名。
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// 参数类型签名的后缀部分，如 "(String, Int32)"。
    /// 用于区分重载。无参数时为 "()"。
    /// </summary>
    public required string ParameterSignature { get; init; }

    /// <summary>
    /// HTTP 方法（GET/POST/PUT/DELETE 等），非 MVC 端点为 null。
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// 路由模板（如 "api/app/user/{id}"），非 MVC 端点为 null。
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>
    /// 错误受众分类。
    /// </summary>
    public required ClientErrorAudience Audience { get; init; }

    /// <summary>
    /// 协议策略（ABP/OAuth/Gateway/SSE 等）。
    /// </summary>
    public required ErrorProtocol Protocol { get; init; }

    /// <summary>
    /// 权限要求列表（类级 + 方法级的组合）。
    /// 空列表表示无显式权限要求（依赖 FallbackPolicy）。
    /// </summary>
    public IReadOnlyList<string> RequiredPermissions { get; init; } = [];

    /// <summary>
    /// 当前响应状态码（固定或条件）。
    /// null 表示根据异常类型由框架决定。
    /// </summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>
    /// 批准的响应 data 字段白名单（客户端 lowerCamelCase）。
    /// 管理端兼容字段保持兼容大小写。
    /// </summary>
    public IReadOnlyList<string> ApprovedDataFields { get; init; } = [];

    /// <summary>
    /// 验收 ID，关联测试用例。
    /// </summary>
    public string? AcceptanceTestId { get; init; }

    /// <summary>
    /// 登记版本（用于漂移检测）。
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// 备注（兼容变化、消费者说明等）。
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// 错误受众分类（方案 4.1 三类策略）。
/// </summary>
public enum ClientErrorAudience
{
    /// <summary>
    /// 客户独立入口、匿名入口。仅公开错误目录。
    /// </summary>
    Public,

    /// <summary>
    /// 明确共用的账户、配置及资源入口。
    /// 固定使用公开规则，管理员调用也不放宽。
    /// </summary>
    Shared,

    /// <summary>
    /// 已登记且经过授权的管理功能。
    /// 保留经过审核的管理业务语义和原结构，不返回原始系统诊断。
    /// </summary>
    AdminCompatibility,
}

/// <summary>
/// 错误协议策略。
/// </summary>
public enum ErrorProtocol
{
    /// <summary>
    /// ABP 标准错误信封：{ error: { code, message, details, data, validationErrors } }。
    /// </summary>
    AbpEnvelope,

    /// <summary>
    /// OAuth 错误格式：{ error, error_description } + WWW-Authenticate 头。
    /// </summary>
    OAuth,

    /// <summary>
    /// TokenGateway 独立协议（GatewayError 形状）。
    /// </summary>
    Gateway,

    /// <summary>
    /// SSE（Server-Sent Events）错误帧。
    /// </summary>
    Sse,

    /// <summary>
    /// SignalR Hub 推送（非目标，登记但不处理）。
    /// </summary>
    SignalR,

    /// <summary>
    /// 文件流（FileResult），错误需先识别 JSON 信封。
    /// </summary>
    FileStream,

    /// <summary>
    /// 纯文本错误消息。
    /// </summary>
    PlainText,

    /// <summary>
    /// 自定义 JSON 形状（需逐个登记）。
    /// </summary>
    CustomJson,
}

/// <summary>
/// 管理端错误端点登记表（方案 4.1 + P0 审查项 b/c）。P1 契约增强。
/// </summary>
/// <remarks>
/// <para>
/// <b>Layering Notice</b>: This static mutable registry is in Domain.Shared for P0 contract definition.
/// P1 will move it to Domain or Application layer with proper DI-based singleton lifecycle.
/// </para>
/// <para>
/// <b>Thread Safety (审查项 b)</b>：Registration period + freeze pattern.
/// After <see cref="Freeze"/> is called, all write attempts (Register/Clear) throw <see cref="InvalidOperationException"/>.
/// Reads after freeze are lock-free and safe for concurrent access because the underlying dictionary becomes immutable.
/// Before freeze, registration must complete on a single thread (module initialization), no concurrent reads.
/// </para>
/// <para>
/// <b>Reflection Exposure (审查项 c)</b>：Class provides two views:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="GetAll"/> returns full registrations (internal: Host/tests only).</description></item>
///   <item><description><see cref="GetPublicView"/> returns public-safe information only (external: catalog consumers).</description></item>
/// </list>
/// <para>
/// Public view excludes reflection details: DeclaringType, MethodName, ParameterSignature, RouteTemplate.
/// These internal fields are only used by Host wiring and tests.
/// </para>
/// </remarks>
public static class AdminErrorEndpointRegistry
{
    private static readonly Dictionary<string, AdminErrorEndpointRegistration> Registrations = new();
    private static readonly object Lock = new();
    private static bool _isFrozen;

    /// <summary>
    /// 是否已冻结。冻结后禁止写入（Register/Clear 抛异常）。
    /// </summary>
    public static bool IsFrozen => _isFrozen;

    /// <summary>
    /// 冻结注册表（P1 Host 启动完成后调用）。
    /// 冻结后注册表变为只读，并发读安全。
    /// 重复调用幂等（不抛异常）。
    /// </summary>
    public static void Freeze()
    {
        lock (Lock)
        {
            _isFrozen = true;
        }
    }

    /// <summary>
    /// 注册端点（模块初始化时调用）。
    /// 冻结后调用抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static void Register(AdminErrorEndpointRegistration registration)
    {
        if (string.IsNullOrEmpty(registration.ExitId))
        {
            throw new ArgumentException("ExitId is required.", nameof(registration));
        }

        lock (Lock)
        {
            if (_isFrozen)
            {
                throw new InvalidOperationException(
                    "Cannot register endpoint after registry is frozen. " +
                    "Ensure all registrations complete during module initialization.");
            }

            if (Registrations.TryGetValue(registration.ExitId, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate endpoint registration: ExitId '{registration.ExitId}' " +
                    $"already registered by {existing.DeclaringType}.{existing.MethodName}.");
            }

            Registrations[registration.ExitId] = registration;
        }
    }

    /// <summary>
    /// 获取所有已注册端点（完整信息，仅限 Host/tests 使用）。
    /// 返回快照副本（防御性拷贝），修改返回集合不影响注册表。
    /// </summary>
    public static IReadOnlyCollection<AdminErrorEndpointRegistration> GetAll()
    {
        lock (Lock)
        {
            return Registrations.Values.ToList();
        }
    }

    /// <summary>
    /// 获取公开视图（仅包含外部安全字段，方案 4.1 + 审查项 c）。
    /// 排除内部反射细节：DeclaringType、MethodName、ParameterSignature、RouteTemplate。
    /// </summary>
    public static IReadOnlyCollection<AdminErrorEndpointPublicInfo> GetPublicView()
    {
        lock (Lock)
        {
            return Registrations.Values
                .Select(r => new AdminErrorEndpointPublicInfo
                {
                    ExitId = r.ExitId,
                    Audience = r.Audience,
                    Protocol = r.Protocol,
                    HttpStatusCode = r.HttpStatusCode,
                    ApprovedDataFields = r.ApprovedDataFields,
                    AcceptanceTestId = r.AcceptanceTestId,
                })
                .ToList();
        }
    }

    /// <summary>
    /// 按 ExitId 查找。
    /// </summary>
    public static AdminErrorEndpointRegistration? FindByExitId(string exitId)
    {
        lock (Lock)
        {
            return Registrations.TryGetValue(exitId, out var registration) ? registration : null;
        }
    }

    /// <summary>
    /// 按路由模板查找。
    /// </summary>
    public static IEnumerable<AdminErrorEndpointRegistration> FindByRouteTemplate(string routeTemplate)
    {
        lock (Lock)
        {
            return Registrations.Values
                .Where(r => r.RouteTemplate?.Equals(routeTemplate, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }
    }

    /// <summary>
    /// 按受众分类查找。
    /// </summary>
    public static IEnumerable<AdminErrorEndpointRegistration> FindByAudience(ClientErrorAudience audience)
    {
        lock (Lock)
        {
            return Registrations.Values
                .Where(r => r.Audience == audience)
                .ToList();
        }
    }

    /// <summary>
    /// 清空登记（仅测试用）。
    /// 冻结后调用抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static void Clear()
    {
        lock (Lock)
        {
            if (_isFrozen)
            {
                throw new InvalidOperationException(
                    "Cannot clear registry after it is frozen. " +
                    "Use ResetForTesting() in test cleanup if needed.");
            }
            Registrations.Clear();
        }
    }

    /// <summary>
    /// 重置注册表（仅测试用，绕过冻结检查）。
    /// 用于测试隔离；生产代码不得调用。
    /// </summary>
    public static void ResetForTesting()
    {
        lock (Lock)
        {
            Registrations.Clear();
            _isFrozen = false;
        }
    }
}

/// <summary>
/// 管理端错误端点公开信息（方案 4.1 + 审查项 c）。P1 契约。
/// 仅包含外部安全字段，不暴露内部反射细节。
/// </summary>
public sealed record AdminErrorEndpointPublicInfo
{
    /// <summary>
    /// 端点唯一标识。
    /// </summary>
    public required string ExitId { get; init; }

    /// <summary>
    /// 错误受众分类。
    /// </summary>
    public required ClientErrorAudience Audience { get; init; }

    /// <summary>
    /// 协议策略（ABP/OAuth/Gateway/SSE 等）。
    /// </summary>
    public required ErrorProtocol Protocol { get; init; }

    /// <summary>
    /// 当前响应状态码（固定或条件）。null 表示框架决定。
    /// </summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>
    /// 批准的响应 data 字段白名单。
    /// </summary>
    public IReadOnlyList<string> ApprovedDataFields { get; init; } = [];

    /// <summary>
    /// 验收 ID，关联测试用例。
    /// </summary>
    public string? AcceptanceTestId { get; init; }
}
