using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户/角色声明管理（自 ClaimTypeAppService.cs 拆出：一类一文件，见重构报告问题 26）。
/// ClaimTypeAppService 负责声明类型的 CRUD，两者是独立应用服务。
/// </summary>
[Authorize]
public class IdentityClaimAppService : AbpAdminAppService, IIdentityClaimAppService
{
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;

    public IdentityClaimAppService(IdentityUserManager userManager, IdentityRoleManager roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [Authorize(IdentityPermissions.Users.Update)]
    public virtual async Task<List<ClaimValueDto>> GetUserClaimsAsync(Guid userId)
    {
        var user = await _userManager.GetByIdAsync(userId);
        var claims = await _userManager.GetClaimsAsync(user);
        return claims.Select(x => new ClaimValueDto { ClaimType = x.Type, ClaimValue = x.Value }).ToList();
    }

    [Authorize(IdentityPermissions.Users.Update)]
    public virtual async Task UpdateUserClaimsAsync(Guid userId, List<ClaimValueDto> claims)
    {
        var user = await _userManager.GetByIdAsync(userId);
        var current = await _userManager.GetClaimsAsync(user);
        await _userManager.RemoveClaimsAsync(user, current);
        await _userManager.AddClaimsAsync(
            user,
            claims.Where(x => !string.IsNullOrWhiteSpace(x.ClaimType))
                .Select(x => new Claim(x.ClaimType, x.ClaimValue ?? string.Empty)));
    }

    [Authorize(IdentityPermissions.Roles.Update)]
    public virtual async Task<List<ClaimValueDto>> GetRoleClaimsAsync(Guid roleId)
    {
        var role = await _roleManager.GetByIdAsync(roleId);
        var claims = await _roleManager.GetClaimsAsync(role);
        return claims.Select(x => new ClaimValueDto { ClaimType = x.Type, ClaimValue = x.Value }).ToList();
    }

    [Authorize(IdentityPermissions.Roles.Update)]
    public virtual async Task UpdateRoleClaimsAsync(Guid roleId, List<ClaimValueDto> claims)
    {
        var role = await _roleManager.GetByIdAsync(roleId);
        var current = await _roleManager.GetClaimsAsync(role);

        // 先算 diff 再增删（原先全删再逐条加，往返多一倍）：
        // 终态语义不变——角色声明集合等于请求集合（按 Type+Value 比对，请求内重复项去重）。
        var requested = claims
            .Where(x => !string.IsNullOrWhiteSpace(x.ClaimType))
            .GroupBy(x => (Type: x.ClaimType, Value: x.ClaimValue ?? string.Empty))
            .Select(g => new Claim(g.Key.Type, g.Key.Value))
            .ToList();

        var requestedPairs = requested.Select(c => (c.Type, c.Value)).ToHashSet();
        var toRemove = current.Where(c => !requestedPairs.Contains((c.Type, c.Value))).ToList();
        var existingPairs = current.Select(c => (c.Type, c.Value)).ToHashSet();
        var toAdd = requested.Where(c => !existingPairs.Contains((c.Type, c.Value))).ToList();

        foreach (var claim in toRemove)
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        foreach (var claim in toAdd)
        {
            await _roleManager.AddClaimAsync(role, claim);
        }
    }
}
