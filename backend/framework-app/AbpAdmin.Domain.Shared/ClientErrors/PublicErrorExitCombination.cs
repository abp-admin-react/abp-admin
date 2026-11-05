using System;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// HTTP 出口允许的（公开码, 分类, HTTP 状态码）组合（方案 5.2）。P1 契约。
/// </summary>
/// <remarks>
/// <para>
/// 每个受控 HTTP 出口声明允许的组合，启动时由 P1 目录校验；运行时由前端 snapshot 匹配。
/// 不为整个 kind 武断绑定唯一状态；允许非基础模块码的多状态组合（显式登记）。
/// </para>
/// <para>
/// 消费者：P1 Domain 启动校验（StaticClientPublicErrorDefinitionValidator）、
/// P1-HTTP 出口投影（组合校验）、P3 快照导出（publicErrorCatalog.json）。
/// </para>
/// </remarks>
public sealed record PublicErrorExitCombination
{
    /// <summary>
    /// 公开稳定码。null 表示无公开码（如 401 的纯认证协议场景）。
    /// </summary>
    public string? PublicCode { get; init; }

    /// <summary>
    /// 公开分类。必须与 PublicCode（如果非 null）的 kind 一致（见 <see cref="PublicErrorExitContract"/> 基础规则）。
    /// </summary>
    public required PublicErrorKind Kind { get; init; }

    /// <summary>
    /// HTTP 状态码。必须 >= 400（错误出口）；HTTP 200 的任务/批量失败结果项使用
    /// 独立的 ClientFailureDto 契约（在 Application.Contracts），不走出口组合校验。
    /// </summary>
    public required int HttpStatus
    {
        get => _httpStatus;
        init
        {
            if (value < 400)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(HttpStatus),
                    "HTTP status must be >= 400 (error exit). HTTP 200 result items use ClientFailureDto contract separately.");
            }
            _httpStatus = value;
        }
    }
    private readonly int _httpStatus;
}

/// <summary>
/// 公开错误出口契约基础规则（方案 5.2 + 方案 7.2）。P1 契约。
/// 提供 <see cref="PublicErrorExitCombination"/> 的静态校验方法。
/// </summary>
/// <remarks>
/// <para>
/// <b>基础规则（与前端 7.2 分类顺序一致）</b>：
/// </para>
/// <list type="bullet">
///   <item><description>401：只能使用 Authentication kind；PublicCode 必须为 null 或 <see cref="ClientErrorCodes.AuthenticationRequired"/>。</description></item>
///   <item><description>403：只能使用 Business 或 Forbidden kind；禁止 Authentication kind。</description></item>
///   <item><description>429：只能使用 RateLimited kind；PublicCode 必须为 null 或 <see cref="ClientErrorCodes.RateLimited"/>。</description></item>
///   <item><description>Authentication kind 只能用于 401；Forbidden 只能用于 403；RateLimited 只能用于 429。</description></item>
///   <item><description>Validation → 400；NotFound → 404；Conflict → 409（ABP 标准映射）。</description></item>
///   <item><description>Server → 5xx；Business → 4xx 或 5xx（由端点登记决定）。</description></item>
///   <item><description>基础公开码（<see cref="ClientErrorCodes"/>）的 kind 必须与 <see cref="ClientErrorKindMapping"/> 一致。</description></item>
///   <item><description>非基础模块码可登记多状态组合，但 kind 不能重新定义基础码 kind。</description></item>
/// </list>
/// <para>
/// <b>前后端一致性</b>：前端 7.2 的归一化逻辑（合法公开码→kind→status 兜底）依赖后端生成的组合；
/// 违反本契约的组合会被前端丢弃并走状态安全兜底（A15）。
/// </para>
/// <para>
/// <b>HTTP 200 特殊处理</b>：组合校验不覆盖 HTTP 200 的任务/批量结果项（见 <see cref="ClientFailureDto"/>）。
/// </para>
/// </remarks>
public static class PublicErrorExitContract
{
    /// <summary>
    /// 检查组合是否符合基础规则（不抛异常）。
    /// </summary>
    public static bool IsAllowed(PublicErrorExitCombination combination)
    {
        if (combination is null)
        {
            return false;
        }

        try
        {
            ValidateCore(combination);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 校验组合是否符合基础规则；不符合时抛出 <see cref="ArgumentException"/>。
    /// </summary>
    public static void Validate(PublicErrorExitCombination combination)
    {
        if (combination is null)
        {
            throw new ArgumentNullException(nameof(combination));
        }

        ValidateCore(combination);
    }

    private static void ValidateCore(PublicErrorExitCombination combination)
    {
        var status = combination.HttpStatus;
        var kind = combination.Kind;
        var code = combination.PublicCode;

        // 1. 状态码必须 >= 400（构造时已校验，这里二次确认）
        if (status < 400)
        {
            throw new ArgumentException(
                $"HTTP status must be >= 400 (error exit), got {status}. " +
                "HTTP 200 result items use ClientFailureDto contract separately.",
                nameof(combination));
        }

        // 2. 基础公开码 kind 一致性
        if (code is not null && ClientErrorKindMapping.GetKind(code) != kind)
        {
            throw new ArgumentException(
                $"Base public code '{code}' kind mismatch: expected {ClientErrorKindMapping.GetKind(code)}, got {kind}.",
                nameof(combination));
        }

        // 3. 401 规则：只能 Authentication；code 只能 null 或 AuthenticationRequired
        if (status == 401)
        {
            if (kind != PublicErrorKind.Authentication)
            {
                throw new ArgumentException(
                    $"HTTP 401 only allows Authentication kind, got {kind}.",
                    nameof(combination));
            }
            if (code is not null && code != ClientErrorCodes.AuthenticationRequired)
            {
                throw new ArgumentException(
                    $"HTTP 401 public code must be null or '{ClientErrorCodes.AuthenticationRequired}', got '{code}'.",
                    nameof(combination));
            }
        }

        // 4. 403 规则：Business 或 Forbidden；禁止 Authentication
        if (status == 403)
        {
            if (kind == PublicErrorKind.Authentication)
            {
                throw new ArgumentException(
                    "HTTP 403 disallows Authentication kind (use Forbidden or Business instead).",
                    nameof(combination));
            }
            if (kind != PublicErrorKind.Business && kind != PublicErrorKind.Forbidden)
            {
                throw new ArgumentException(
                    $"HTTP 403 only allows Business or Forbidden kind, got {kind}.",
                    nameof(combination));
            }
        }

        // 5. 429 规则：只能 RateLimited；code 只能 null 或 RateLimited
        if (status == 429)
        {
            if (kind != PublicErrorKind.RateLimited)
            {
                throw new ArgumentException(
                    $"HTTP 429 only allows RateLimited kind, got {kind}.",
                    nameof(combination));
            }
            if (code is not null && code != ClientErrorCodes.RateLimited)
            {
                throw new ArgumentException(
                    $"HTTP 429 public code must be null or '{ClientErrorCodes.RateLimited}', got '{code}'.",
                    nameof(combination));
            }
        }

        // 6. Kind → Status 约束
        if (kind == PublicErrorKind.Authentication && status != 401)
        {
            throw new ArgumentException(
                $"Authentication kind requires HTTP 401, got {status}.",
                nameof(combination));
        }
        if (kind == PublicErrorKind.Forbidden && status != 403)
        {
            throw new ArgumentException(
                $"Forbidden kind requires HTTP 403, got {status}.",
                nameof(combination));
        }
        if (kind == PublicErrorKind.RateLimited && status != 429)
        {
            throw new ArgumentException(
                $"RateLimited kind requires HTTP 429, got {status}.",
                nameof(combination));
        }

        // 7. Validation → 400；NotFound → 404；Conflict → 409
        if (kind == PublicErrorKind.Validation && status != 400)
        {
            throw new ArgumentException(
                $"Validation kind requires HTTP 400, got {status}.",
                nameof(combination));
        }
        if (kind == PublicErrorKind.NotFound && status != 404)
        {
            throw new ArgumentException(
                $"NotFound kind requires HTTP 404, got {status}.",
                nameof(combination));
        }
        if (kind == PublicErrorKind.Conflict && status != 409)
        {
            throw new ArgumentException(
                $"Conflict kind requires HTTP 409, got {status}.",
                nameof(combination));
        }

        // 8. Server → 5xx
        if (kind == PublicErrorKind.Server && (status < 500 || status > 599))
        {
            throw new ArgumentException(
                $"Server kind requires HTTP 5xx, got {status}.",
                nameof(combination));
        }

        // 9. Business → 4xx 或 5xx
        if (kind == PublicErrorKind.Business && (status < 400 || status > 599))
        {
            throw new ArgumentException(
                $"Business kind requires HTTP 4xx or 5xx, got {status}.",
                nameof(combination));
        }
    }
}
