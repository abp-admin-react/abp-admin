using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Roles;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.Users;

namespace AbpAdmin.Account;

/// <summary>
/// T2.7 模拟登录核心逻辑（Domain 层，Application.Tests 可直接覆盖）。
/// 负责目标用户/租户解析、impersonator claim 构建、返回原身份校验；
/// 令牌签发出 HttpApi.Host 的 ImpersonationTokenExtensionGrant 完成，
/// 这里产出的 claim 会被它写入令牌，审计日志的 ImpersonatorUserId 列即从该 claim 取值。
/// </summary>
public class ImpersonationManager : DomainService
{
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IRepository<IdentityRole, Guid> _roleRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentUser _currentUser;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    public ImpersonationManager(
        IdentityUserManager userManager,
        IRepository<IdentityUser, Guid> userRepository,
        IRepository<IdentityRole, Guid> roleRepository,
        ITenantRepository tenantRepository,
        IDataFilter dataFilter,
        ICurrentUser currentUser,
        IStringLocalizer<AbpAdminResource> localizer)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _tenantRepository = tenantRepository;
        _dataFilter = dataFilter;
        _currentUser = currentUser;
        _localizer = localizer;
    }

    /// <summary>
    /// 用户模拟：host 上下文可模拟任意租户的用户，租户上下文只能模拟本租户用户。
    /// 并构建应写入令牌的 impersonator claim（原始身份 = 当前用户/当前租户）。
    /// </summary>
    public virtual async Task<ImpersonationTarget> CreateUserImpersonationAsync(Guid targetUserId)
    {
        IdentityUser? targetUser;
        // 跨租户解析：host 上下文的多租户过滤器看不到租户用户。
        // 串行路径上的 Disable 是合法用法（00-overview 6.3 只禁并行分支内）。
        using (_dataFilter.Disable<IMultiTenant>())
        {
            targetUser = await _userManager.FindByIdAsync(targetUserId.ToString());
        }

        if (targetUser == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
        }

        // 租户边界（安全）：Impersonation.User 权限对租户侧同样可授予，若不校验，
        // 租户 A 管理员可传入宿主/租户 B 用户 Id 直接签发其令牌（提权）。
        // 统一抛 NotFound 而非越权专用错误码，避免探测其他租户用户是否存在。
        if (CurrentTenant.Id != null && targetUser.TenantId != CurrentTenant.Id)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
        }

        // 停用账户不可被模拟（fail closed，与租户模拟路径的 u.IsActive 过滤对齐）：
        // 管理员停用某账户后，持有 Impersonation.User 权限者不应仍能以该账户身份操作。
        // 同样抛 NotFound，不暴露「存在但停用」与「不存在」的区别。
        if (!targetUser.IsActive)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
        }

        return new ImpersonationTarget(targetUser, BuildImpersonatorClaims(_currentUser.Id, CurrentTenant.Id));
    }

    /// <summary>
    /// 租户模拟：以目标租户 admin 角色的第一个有效用户身份进入（与 ABP Pro 语义一致）。
    /// 注意不能靠 CurrentTenant.Change 切租户后查询——当前请求的 DbContext 已按原租户建好
    /// （00-overview 6.5），只能临时关掉多租户过滤器、按 TenantId 显式过滤。
    /// </summary>
    public virtual async Task<ImpersonationTarget> CreateTenantImpersonationAsync(Guid targetTenantId)
    {
        var tenant = await _tenantRepository.FindAsync(targetTenantId);
        if (tenant == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTenantNotFound);
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var roleQueryable = await _roleRepository.GetQueryableAsync();
            // 管理员角色名走框架常量（NormalizedRoleName 按 upper-invariant 归一化），不再硬编码 "ADMIN"
            var adminRoleNormalizedName = AbpRoleConsts.AdminRoleName.ToUpperInvariant();
            var adminRole = await AsyncExecuter.FirstOrDefaultAsync(
                roleQueryable.Where(r => r.TenantId == targetTenantId && r.NormalizedName == adminRoleNormalizedName));
            if (adminRole == null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTenantAdminNotFound);
            }

            var userQueryable = await _userRepository.GetQueryableAsync();
            var targetUser = await AsyncExecuter.FirstOrDefaultAsync(
                userQueryable.Where(u => u.TenantId == targetTenantId && u.IsActive &&
                                         u.Roles.Any(ur => ur.RoleId == adminRole.Id)));
            if (targetUser == null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTenantAdminNotFound);
            }

            return new ImpersonationTarget(targetUser, BuildImpersonatorClaims(_currentUser.Id, CurrentTenant.Id));
        }
    }

    /// <summary>
    /// T4.2：进入权限委托。当前用户必须是委托的 TargetUserId，签入 SourceUserId。
    /// </summary>
    public virtual async Task<ImpersonationTarget> CreateDelegationImpersonationAsync(Guid delegationId)
    {
        var currentUserId = _currentUser.GetId();
        var manager = LazyServiceProvider.LazyGetRequiredService<IdentityUserDelegationManager>();
        var delegation = await manager.FindActiveDelegationByIdAsync(delegationId);
        if (delegation == null || delegation.TargetUserId != currentUserId)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.DelegationNotActive);
        }

        IdentityUser? sourceUser;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            sourceUser = await _userManager.FindByIdAsync(delegation.SourceUserId.ToString());
        }

        if (sourceUser == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
        }

        // 租户边界：委托行属于当前租户，但 SourceUserId 理论上可指向宿主/他租户用户——防御性同校验
        if (CurrentTenant.Id != null && sourceUser.TenantId != CurrentTenant.Id)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
        }

        return new ImpersonationTarget(sourceUser, BuildImpersonatorClaims(currentUserId, CurrentTenant.Id));
    }

    /// <summary>
    /// 校验当前会话处于模拟状态（令牌带 impersonator claim），并返回原始身份用户。
    /// 普通令牌（无 impersonator claim）调用必须被拒——抛 <see cref="AbpAuthorizationException"/>（403），
    /// 否则任何人都能借此端点提权。原始用户可能是 host 用户而当前租户上下文是目标租户，查询同样要关过滤器。
    /// </summary>
    public virtual async Task<IdentityUser> GetImpersonatorUserOrThrowAsync()
    {
        var impersonatorUserId = _currentUser.FindClaimValue(AbpClaimTypes.ImpersonatorUserId);
        if (impersonatorUserId.IsNullOrWhiteSpace())
        {
            // 走本地化资源（原为硬编码中文，英文租户不可读），与文件内其余错误的处理方式对齐
            throw new AbpAuthorizationException(_localizer["Account:NotInImpersonation"]);
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var originalUser = await _userManager.FindByIdAsync(impersonatorUserId);
            if (originalUser == null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonatorUserNotFound);
            }

            return originalUser;
        }
    }

    /// <summary>
    /// 构建写入模拟令牌的 impersonator claim。用框架常量（AbpClaimTypes），
    /// 审计日志的 ImpersonatorUserId / ImpersonatorTenantId 列正是从这两个 claim 取值。
    /// </summary>
    public virtual List<Claim> BuildImpersonatorClaims(Guid? impersonatorUserId, Guid? impersonatorTenantId)
    {
        var claims = new List<Claim>();
        if (impersonatorUserId.HasValue)
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorUserId, impersonatorUserId.Value.ToString()));
        }
        if (impersonatorTenantId.HasValue)
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorTenantId, impersonatorTenantId.Value.ToString()));
        }
        return claims;
    }
}

/// <summary>
/// 模拟登录的解析结果：要签入的目标用户 + 应写入令牌的 impersonator claim。
/// </summary>
public class ImpersonationTarget
{
    public IdentityUser User { get; }

    public List<Claim> ImpersonatorClaims { get; }

    public ImpersonationTarget(IdentityUser user, List<Claim> impersonatorClaims)
    {
        User = user;
        ImpersonatorClaims = impersonatorClaims;
    }
}
