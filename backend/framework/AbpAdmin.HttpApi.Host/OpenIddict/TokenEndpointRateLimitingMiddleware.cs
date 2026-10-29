using System;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// /connect/token 端点级限流（round4 security F1；安全审计 M-2 扩展到全 grant 类型）。
/// password grant 直打端点可绕过 Login 页面侧限流与页面验证码（ROPC 早已从 SPA 客户端
/// 移除，密码换票唯一通道是机密客户端 AbpAdmin_TestCli——但其令牌铸造面仍需覆盖），
/// Identity 的按账户锁定防不了跨账户 spraying——Login 策略（IP+邮箱双规则）继续覆盖它。
/// M-2：其余 grant（refresh_token / authorization_code / client_credentials /
/// passwordless / impersonation / linked_account）此前完全不限流。统一按 IP 走宽松的
/// TokenEndpoint 策略（默认 300 次/5 分钟；2026-10-10 实测：单 IP 第 301 发精确触发）
/// 兜底——防脚本高频打端点消耗资源与探测；阈值给服务端自回调（impersonation/token
/// exchange 回环）与 E2E 测试留足余量。猜码窄面（passwordless 的验证码校验）由扩展授权
/// 内部的 VerificationCodeVerify 策略把守，本闸不重复设防。
///
/// 不能用 OpenIddict 事件处理器实现：Extract/ValidateTokenRequest 在 UseAuthentication
/// 内、早于 UseMultiTenancy 执行（见 TenantActivationOpenIddictServerHandler 头注释的
/// 管线次序核实），事件里没有租户上下文。故在 UseAuthentication 之前挂本中间件：
/// 仅对 POST /connect/token 且 HasFormContentType 的表单请求计数（此处租户上下文
/// 未解析，IP 规则与租户无关；password 的邮箱规则按 username 参数分区、计数落 host
/// 分区，与页面侧同一策略名但分区独立，两道闸各自有界）。
/// 超限抛 AbpAdminOperationRateLimitingException，此处转成 429 + Retry-After 的
/// JSON 响应（端点直答，语义与 ABP 对该异常的处理一致；页面侧仍走全局异常处理）。
/// ReadFormAsync 结果被 ASP.NET Core 缓存，不影响后续 OpenIddict 再读表单。
/// </summary>
public class TokenEndpointRateLimitingMiddleware
{
    private const string TokenEndpointPath = "/connect/token";
    private const string PasswordGrantType = "password";

    private readonly RequestDelegate _next;

    /// <summary>与 OpenIddict InferEndpointType 同口径的端点匹配：OrdinalIgnoreCase + 容忍一个尾斜杠。
    /// 双尾斜杠不被 OpenIddict 受理（请求到不了端点），门必须同样不认——否则匿名主体可用
    /// 「//connect/token//」空耗每 IP 共享桶（NV-16，单测钉死）。TrimEnd 会裁掉全部尾斜杠，
    /// 必须只裁一个。</summary>
    internal static bool IsTokenEndpointPath(PathString path)
    {
        var value = path.Value;
        if (value is null)
        {
            return false;
        }

        if (value.Length > 1 && value[^1] == '/')
        {
            value = value[..^1];
        }

        return value.Equals(TokenEndpointPath, StringComparison.OrdinalIgnoreCase);
    }

    public TokenEndpointRateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 安全审计 run-1（TV-06b）：门限与受理端匹配口径对齐。OpenIddict 7.7 InferEndpointType
        // 按 OrdinalIgnoreCase 比对且容忍恰好一个尾斜杠——原门限用 Path 精确等值，/connect/token/
        // 会绕过限流计数直达端点（实验证实）。归一化后与受理端同口径。
        if (HttpMethods.IsPost(context.Request.Method) &&
            IsTokenEndpointPath(context.Request.Path) &&
            context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync();
            var checker = context.RequestServices.GetRequiredService<IOperationRateLimitingChecker>();

            // M-2：全部 grant 类型先过 IP 兜底闸（宽阈值）
            if (await TryRejectRateLimitedAsync(
                    context, checker, OperationRateLimitingPolicyNames.TokenEndpoint, parameter: null))
            {
                return;
            }

            // password grant 叠加更严的 Login 策略（邮箱规则按 username 参数分区）
            if (string.Equals(form["grant_type"].ToString(), PasswordGrantType, StringComparison.OrdinalIgnoreCase))
            {
                if (await TryRejectRateLimitedAsync(
                        context, checker, OperationRateLimitingPolicyNames.Login, form["username"].ToString()))
                {
                    return;
                }
            }
        }

        await _next(context);
    }

    private static async Task<bool> TryRejectRateLimitedAsync(
        HttpContext context,
        IOperationRateLimitingChecker checker,
        string policyName,
        string? parameter)
    {
        try
        {
            await checker.CheckAsync(policyName, parameter);
            return false;
        }
        catch (AbpAdminOperationRateLimitingException exception)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            // Checker 写入的是 TotalSeconds（double，如 "293.7412"）——必须按 double 解析再
            // 取整：此前 int.TryParse 解析小数串恒失败，Retry-After 头从未真正下发过
            //（安全审计 L-5 的运行时观察成立，静态"已设置"只对了一半）。
            if (exception.Data.Contains("RetryAfterSeconds") &&
                double.TryParse(exception.Data["RetryAfterSeconds"]?.ToString(), out var retryAfterSeconds))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfterSeconds)).ToString();
            }

            await context.Response.WriteAsJsonAsync(new
            {
                error = "rate_limited",
                error_description = "Too many token requests. Please retry later."
            });
            return true;
        }
    }
}
