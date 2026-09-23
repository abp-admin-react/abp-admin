using System;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// /connect/token 端点级限流（round4 security F1，round3 遗留专项）。
/// password grant 直打端点可同时绕过 Login 页面侧限流与页面验证码（AbpAdmin_App 是
/// Public 客户端、无需密钥即可用 password grant 换票），Identity 的按账户锁定防不了
/// 跨账户 spraying——Login 策略（IP+邮箱双规则）此前唯一消费者在 Login 页面，端点裸奔。
///
/// 不能用 OpenIddict 事件处理器实现：Extract/ValidateTokenRequest 在 UseAuthentication
/// 内、早于 UseMultiTenancy 执行（见 TenantActivationOpenIddictServerHandler 头注释的
/// 管线次序核实），事件里没有租户上下文。故在 UseAuthentication 之前挂本中间件：
/// 仅对 POST /connect/token 且 grant_type=password 的表单请求计数，复用 Login 策略
/// （IP 规则与租户无关；邮箱规则按 username 参数分区，与页面侧同一策略名但计数分区
/// 不同——此处租户上下文未解析，计数落在 host 分区，两道闸各自独立有界）。
/// 超限抛 AbpAdminOperationRateLimitingException，此处转成 429 + Retry-After 的
/// JSON 响应（端点直答，语义与 ABP 对该异常的处理一致；页面侧仍走全局异常处理）。
/// ReadFormAsync 结果被 ASP.NET Core 缓存，不影响后续 OpenIddict 再读表单。
/// </summary>
public class TokenEndpointRateLimitingMiddleware
{
    private const string TokenEndpointPath = "/connect/token";
    private const string PasswordGrantType = "password";

    private readonly RequestDelegate _next;

    public TokenEndpointRateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals(TokenEndpointPath, StringComparison.OrdinalIgnoreCase) &&
            context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync();
            if (string.Equals(form["grant_type"].ToString(), PasswordGrantType, StringComparison.OrdinalIgnoreCase))
            {
                var checker = context.RequestServices.GetRequiredService<IOperationRateLimitingChecker>();
                try
                {
                    await checker.CheckAsync(
                        OperationRateLimitingPolicyNames.Login,
                        form["username"].ToString());
                }
                catch (AbpAdminOperationRateLimitingException exception)
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    if (exception.Data.Contains("RetryAfterSeconds") &&
                        int.TryParse(exception.Data["RetryAfterSeconds"]?.ToString(), out var retryAfterSeconds))
                    {
                        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                    }

                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "rate_limited",
                        error_description = "Too many login attempts. Please retry later."
                    });
                    return;
                }
            }
        }

        await _next(context);
    }
}
