using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OpenIddict;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AbpAdmin.ExtensionGrants;

/// <summary>
/// 关联账号切换扩展授权：grant_type = "linked-account"，参数 target_user_id。
/// 以调用方访问令牌识别当前用户，grant 内复查「当前用户与目标账号存在关联」（双向匹配 +
/// 两侧租户一致——/connect/token 是公开端点，不能只靠上游 AppService 的 [Authorize]；
/// host 上下文的租户过滤器是关闭的，漏掉对端租户校验等于 host 账号可切换进任意租户账号），
/// 通过后为目标账号签发完整会话——与 impersonation 不同：不写 impersonator claim、
/// 保留 offline_access（切换是真实登录，不是模拟）；目标锁定/停用一律拒绝（fail closed）。
/// </summary>
public class LinkedAccountExtensionGrant : ITokenExtensionGrant
{
    public const string ExtensionGrantName = AbpAdminOpenIddictDefaults.GrantTypes.LinkedAccount;

    public string Name => ExtensionGrantName;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var currentUser = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.IsAuthenticated)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Authentication required");
        }

        var targetUserIdParam = context.Request.GetParameter("target_user_id")?.ToString();
        if (string.IsNullOrWhiteSpace(targetUserIdParam)
            || !Guid.TryParse(targetUserIdParam, out var targetUserId))
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidRequest, "target_user_id is required");
        }

        var currentUserId = currentUser.GetId();
        if (currentUserId == targetUserId)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Target account is the current account");
        }

        var linkUserRepository = context.HttpContext.RequestServices
            .GetRequiredService<IIdentityLinkUserRepository>();
        var userManager = context.HttpContext.RequestServices.GetRequiredService<IdentityUserManager>();

        // 关联复查：双向匹配（绑定时不区分 source/target 方向）+ 当前侧租户一致 + 对端租户一致。
        // 对端租户校验不可省：host 上下文的 IMultiTenant 过滤器是关闭的，缺了它 host 账号
        // 可凭一条跨租户脏关联切换进租户账号（绕过模拟登录权限门禁与 impersonator 审计）
        var currentTenantId = currentUser.TenantId;
        var links = await linkUserRepository.GetListAsync(
            new IdentityLinkUserInfo(currentUserId, currentTenantId));

        var link = links.FirstOrDefault(l =>
            (l.SourceUserId == currentUserId && l.SourceTenantId == currentTenantId && l.TargetUserId == targetUserId)
            || (l.TargetUserId == currentUserId && l.TargetTenantId == currentTenantId && l.SourceUserId == targetUserId));

        if (link == null)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Accounts are not linked");
        }

        var targetTenantId = link.SourceUserId == targetUserId ? link.SourceTenantId : link.TargetTenantId;
        if (targetTenantId != currentTenantId)
        {
            // 关联记录对端与调用方不同租户：v1 语义外，拒绝切换
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Accounts are not linked");
        }

        // 目标账号与当前用户同租户，在当前租户上下文内解析；停用/锁定一律拒绝
        //（与 PasswordlessLoginManager 消费时刻的口径一致：锁定不可被关联关系绕过）
        IdentityUser targetUser;
        try
        {
            targetUser = await userManager.GetByIdAsync(targetUserId);
        }
        catch (BusinessException)
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Target account not found");
        }

        if (!targetUser.IsActive || await userManager.IsLockedOutAsync(targetUser))
        {
            return ExtensionGrantSupport.CreateErrorResult(OpenIddictConstants.Errors.InvalidGrant, "Target account is inactive");
        }

        var userClaimsPrincipalFactory = context.HttpContext.RequestServices
            .GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();
        var claimsPrincipal = await userClaimsPrincipalFactory.CreateAsync(targetUser);

        return await ExtensionGrantSupport.SignInWithScopesAsync(context, claimsPrincipal, stripOfflineAccess: false);
    }
}
