using System;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 安全审计极端轮（H-1 补强）：授权码端点强制 PKCE 用 S256，拒绝 plain。
/// 实测 OpenIddict 7.5 默认放行 code_challenge_method=plain（未知 method 才 400）——
/// plain 把 verifier 明文放进 authorize 请求，能观察到授权请求（代理日志/Referer）并截获
/// 授权码的攻击者即可完成兑换，PKCE 对该向量的防护失效。SPA（oidc-client-ts）恒用 S256，
/// 本守卫只影响攻击者与误配置客户端。RFC 7636 §4.4/OAuth 2.1 草案：公共客户端应使用 S256。
/// 校验点在 ValidateAuthorizationRequest（登录跳转之前——实测 plain 会在该阶段通过并 302
/// 登录页，必须在进入登录流程前拒绝）。
/// </summary>
public class RequireS256CodeChallengeHandler : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseSingletonHandler<RequireS256CodeChallengeHandler>()
            .SetOrder(int.MinValue + 100_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        // 只管授权码流程（hybrid 也带 code，一并覆盖）；无 code_challenge 的请求由客户端的
        // PKCE requirement 拒（AbpAdmin_App 已强制），不在此重复设防。
        var responseTypes = context.Request.GetResponseTypes();
        var usesCode = responseTypes.Contains(ResponseTypes.Code);
        var challenge = context.Request.CodeChallenge;
        if (!usesCode || string.IsNullOrEmpty(challenge))
        {
            return default;
        }

        var method = context.Request.CodeChallengeMethod;
        if (string.IsNullOrEmpty(method))
        {
            // RFC 7636：缺省 method 按 plain 处理——同样拒绝，要求显式 S256
            context.Reject(
                error: Errors.InvalidRequest,
                description: "The code_challenge_method parameter is required and must be set to S256.");
            return default;
        }

        // OpenIddict 7.5 的常量表没有 S256 条目（plain/S256 字面量见 RFC 7636），直接按字面量比较
        if (!string.Equals(method, "S256", StringComparison.Ordinal))
        {
            context.Reject(
                error: Errors.InvalidRequest,
                description: "Only the S256 code_challenge_method is accepted.");
            return default;
        }

        return default;
    }
}
