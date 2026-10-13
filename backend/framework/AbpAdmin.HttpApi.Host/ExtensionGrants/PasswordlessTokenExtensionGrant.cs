using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Account;
using AbpAdmin.OpenIddict;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace AbpAdmin.ExtensionGrants;

/// <summary>
/// T2.7 无密码登录扩展授权：支持邮箱验证码与 Magic Link 两种方式。
/// grant_type = "passwordless"，参数：email + code 或 email + magic_link_token。
/// 凭据的一次性语义：任一路径成功后整条缓存记录删除，另一路径立即失效。
/// </summary>
public class PasswordlessTokenExtensionGrant : ITokenExtensionGrant
{
    // 字面量统一到 Domain.Shared 常量（与 Domain 种子、DTO 校验同源），避免三处漂移
    public const string ExtensionGrantName = AbpAdminOpenIddictDefaults.GrantTypes.Passwordless;

    public string Name => ExtensionGrantName;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var email = context.Request.GetParameter("email")?.ToString();
        var code = context.Request.GetParameter("code")?.ToString();
        var magicLinkToken = context.Request.GetParameter("magic_link_token")?.ToString();

        if (string.IsNullOrWhiteSpace(email))
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidRequest, "email is required");
        }

        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(magicLinkToken))
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidRequest, "code or magic_link_token is required");
        }

        var passwordlessLoginManager = context.HttpContext.RequestServices.GetRequiredService<PasswordlessLoginManager>();
        var userManager = context.HttpContext.RequestServices.GetRequiredService<IdentityUserManager>();

        IdentityUser? user;
        try
        {
            // 校验并消费一次性凭据（含校验侧限流），成功即整条删除、另一路径立即失效
            user = await passwordlessLoginManager.ValidateAndConsumeAsync(email, code, magicLinkToken);
        }
        catch (AbpAdminOperationRateLimitingException)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Too many attempts, please retry later");
        }

        if (user == null)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Invalid or expired credentials");
        }

        return await SignInUserAsync(context, userManager, user);
    }

    private static async Task<IActionResult> SignInUserAsync(
        ExtensionGrantContext context,
        IdentityUserManager userManager,
        IdentityUser user)
    {
        var userClaimsPrincipalFactory = context.HttpContext.RequestServices
            .GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();
        var claimsPrincipal = await userClaimsPrincipalFactory.CreateAsync(user);

        // 真实登录会话：默认 scope 保留 offline_access（签发 refresh token）
        return await ExtensionGrantSupport.SignInWithScopesAsync(context, claimsPrincipal, stripOfflineAccess: false);
    }

}
