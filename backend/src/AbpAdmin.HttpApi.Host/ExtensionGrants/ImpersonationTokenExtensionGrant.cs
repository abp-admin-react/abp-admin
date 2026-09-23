using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Account;
using AbpAdmin.OpenIddict;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace AbpAdmin.ExtensionGrants;

/// <summary>
/// T2.7 模拟登录扩展授权：支持租户模拟与用户模拟。
/// grant_type = "impersonation"，参数：
///   - 租户模拟：tenant_id（以目标租户 admin 用户身份进入，需 AbpAdmin.Impersonation.Tenant 权限）
///   - 用户模拟：user_id（需 AbpAdmin.Impersonation.User 权限）
///   - 返回原身份：无额外参数，但当前令牌必须带有 impersonator claim
/// 目标解析、权限复查与 impersonator claim（AbpClaimTypes.ImpersonatorUserId/TenantId）
/// 由 Domain 层的 ImpersonationManager 完成，审计日志的 ImpersonatorUserId 列即从该 claim 取值。
/// </summary>
public class ImpersonationTokenExtensionGrant : ITokenExtensionGrant
{
    // 字面量统一到 Domain.Shared 常量（与 Domain 种子、DTO 校验同源），避免三处漂移
    public const string ExtensionGrantName = AbpAdminOpenIddictDefaults.GrantTypes.Impersonation;

    public string Name => ExtensionGrantName;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.IsAuthenticated)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Authentication required");
        }

        var impersonationManager = context.HttpContext.RequestServices.GetRequiredService<ImpersonationManager>();

        var tenantIdParam = context.Request.GetParameter("tenant_id")?.ToString();
        var userIdParam = context.Request.GetParameter("user_id")?.ToString();
        var delegationIdParam = context.Request.GetParameter("delegation_id")?.ToString();

        // 返回原身份：无参数，但当前令牌必须带有 impersonator claim（在 manager 内强校验，防提权）
        if (string.IsNullOrWhiteSpace(tenantIdParam)
            && string.IsNullOrWhiteSpace(userIdParam)
            && string.IsNullOrWhiteSpace(delegationIdParam))
        {
            try
            {
                var originalUser = await impersonationManager.GetImpersonatorUserOrThrowAsync();
                return await SignInUserAsync(context, originalUser, null);
            }
            catch (AbpAuthorizationException)
            {
                return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "No impersonation session found");
            }
            catch (BusinessException)
            {
                return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Original user not found");
            }
        }

        // /connect/token 是公开端点，权限不能只靠上游 AppService 的 [Authorize]，grant 内必须复查
        var authorizationService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();

        ImpersonationTarget target;
        try
        {
            if (!string.IsNullOrWhiteSpace(delegationIdParam))
            {
                if (!Guid.TryParse(delegationIdParam, out var delegationId))
                {
                    return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidRequest, "Invalid delegation_id format");
                }

                target = await impersonationManager.CreateDelegationImpersonationAsync(delegationId);
            }
            else if (!string.IsNullOrWhiteSpace(userIdParam))
            {
                if (!Guid.TryParse(userIdParam, out var targetUserId))
                {
                    return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidRequest, "Invalid user_id format");
                }

                var authResult = await authorizationService.AuthorizeAsync(
                    context.HttpContext.User, AbpAdminPermissions.Impersonation.User);
                if (!authResult.Succeeded)
                {
                    return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Impersonation permission denied");
                }

                target = await impersonationManager.CreateUserImpersonationAsync(targetUserId);
            }
            else
            {
                if (!Guid.TryParse(tenantIdParam, out var targetTenantId))
                {
                    return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidRequest, "Invalid tenant_id format");
                }

                var authResult = await authorizationService.AuthorizeAsync(
                    context.HttpContext.User, AbpAdminPermissions.Impersonation.Tenant);
                if (!authResult.Succeeded)
                {
                    return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Impersonation permission denied");
                }

                target = await impersonationManager.CreateTenantImpersonationAsync(targetTenantId);
            }
        }
        catch (BusinessException ex)
        {
            // BusinessException 的 Message 是未本地化的原始文本，对外给稳定的错误码更有用
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, ex.Code ?? ex.Message);
        }

        return await SignInUserAsync(context, target.User, target.ImpersonatorClaims);
    }

    private static async Task<IActionResult> SignInUserAsync(
        ExtensionGrantContext context,
        IdentityUser user,
        List<Claim>? impersonatorClaims)
    {
        var userClaimsPrincipalFactory = context.HttpContext.RequestServices
            .GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();

        // 目标用户可能在其他租户，而当前请求的 DbContext 已按当前租户建好（00-overview 6.5），
        // 串行路径上临时关掉多租户过滤器，保证用户与角色都能解析到
        var dataFilter = context.HttpContext.RequestServices.GetRequiredService<IDataFilter>();
        ClaimsPrincipal claimsPrincipal;
        using (dataFilter.Disable<IMultiTenant>())
        {
            claimsPrincipal = await userClaimsPrincipalFactory.CreateAsync(user);
        }

        // 写入 impersonator claim，用于审计日志与返回原身份
        if (impersonatorClaims != null)
        {
            foreach (var claim in impersonatorClaims)
            {
                claimsPrincipal.AddClaim(claim.Type, claim.Value);
            }
        }

        // 模拟票剥离 offline_access：即便请求携带也不签发 refresh token（短会话语义）
        return await ExtensionGrantSupport.SignInWithScopesAsync(context, claimsPrincipal, stripOfflineAccess: true);
    }
}
