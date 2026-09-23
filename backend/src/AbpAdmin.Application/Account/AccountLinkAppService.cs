using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.OpenIddict;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Identity;
// IdentityUser 默认指 ABP 的实体（Microsoft.AspNetCore.Identity 同名类型仅 PasswordHasher 等基础设施用）
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace AbpAdmin.Account;

/// <summary>
/// Linked Accounts（关联账号，对标 ABP Pro，v1 限同租户）。
/// 安全要点：类级 [Authorize]（四个端点均为本人操作面，授权是显式契约而非 GetId 的副作用）；
/// 绑定必须提供目标账号密码，且全部失败路径（未知/跨租户/停用/锁定/密码错）统一错误码并
/// 对齐耗时（防枚举延迟 + 一次哈希运算），错误密码计入目标账号锁定（防跨账号爆破）；
/// host 上下文的租户过滤器是关闭的，同租户绑定必须显式断言 target.TenantId == CurrentTenant.Id，
/// 否则 host 账号可绑定并切换进租户账号（绕过模拟登录权限门禁与 impersonator 审计）；
/// 切换取票在 /connect/token 的 linked-account 扩展授权内复查关联关系（公开端点不能只靠上游）。
/// </summary>
[Authorize]
public class AccountLinkAppService : AbpAdminAppService, IAccountLinkAppService
{
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;
    private readonly Volo.Abp.Domain.Repositories.IRepository<IdentityUser, Guid> _userQueryRepository;
    private readonly IdentityLinkUserManager _linkUserManager;
    private readonly IIdentityLinkUserRepository _linkUserRepository;
    private readonly LinkedAccountTokenExchanger _tokenExchanger;

    /// <summary>
    /// 当前请求的访问令牌（Web 环境由 HttpApi.Host 的实现注入；测试环境未注册时保持 Null 实现）。
    /// 与 AccountProAppService 相同的属性注入模式。
    /// </summary>
    public ICurrentAccessTokenProvider AccessTokenProvider { get; set; } = new NullCurrentAccessTokenProvider();

    public AccountLinkAppService(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        Volo.Abp.Domain.Repositories.IRepository<IdentityUser, Guid> userQueryRepository,
        IdentityLinkUserManager linkUserManager,
        IIdentityLinkUserRepository linkUserRepository,
        LinkedAccountTokenExchanger tokenExchanger)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _userQueryRepository = userQueryRepository;
        _linkUserManager = linkUserManager;
        _linkUserRepository = linkUserRepository;
        _tokenExchanger = tokenExchanger;
    }

    public virtual async Task<IReadOnlyList<LinkedAccountDto>> GetListAsync()
    {
        var currentUserId = CurrentUser.GetId();
        var links = await _linkUserManager.GetListAsync(
            new IdentityLinkUserInfo(currentUserId, CurrentTenant.Id));

        // 批量解析对端用户（审查轮消除 N+1）；解析失败（历史脏数据）跳过并告警，不让列表 500
        var otherIds = links
            .Select(l => l.SourceUserId == currentUserId ? l.TargetUserId : l.SourceUserId)
            .Distinct()
            .ToList();

        var usersById = new Dictionary<Guid, IdentityUser>();
        if (otherIds.Count > 0)
        {
            var queryable = await _userQueryRepository.GetQueryableAsync();
            var peers = await AsyncExecuter.ToListAsync(
                queryable.Where(u => otherIds.Contains(u.Id)));
            usersById = peers.ToDictionary(u => u.Id);
        }

        var result = new List<LinkedAccountDto>();
        foreach (var link in links)
        {
            var otherId = link.SourceUserId == currentUserId ? link.TargetUserId : link.SourceUserId;
            if (!usersById.TryGetValue(otherId, out var other))
            {
                Logger.LogWarning("关联账号记录 {LinkId} 的对端用户 {UserId} 无法在当前租户解析，已跳过", link.Id, otherId);
                continue;
            }

            result.Add(MapToDto(link, other));
        }

        return result;
    }

    public virtual async Task<LinkedAccountDto> LinkAsync(LinkAccountInput input)
    {
        var currentUser = await _userManager.GetByIdAsync(CurrentUser.GetId());

        // v1 限同租户：解析限定当前租户。注意 host 上下文（CurrentTenant.Id == null）的
        // IMultiTenant 过滤器是关闭的，FindByName/FindByEmail 能解析到任意租户用户——
        // 解析结果必须再显式断言租户一致，否则 host 账号可绑定租户账号并经 grant 切换过去。
        var target = await _userManager.FindByNameAsync(input.UserNameOrEmail)
                     ?? await _userRepository.FindByNormalizedEmailAsync(input.UserNameOrEmail.ToUpperInvariant());

        // 统一口径：账号不存在 / 跨租户 / 停用 / 锁定 / 密码错 → 同一错误码 + 对齐耗时（见 DenyAsync），
        // 不向调用方区分具体原因（防枚举，含时序侧信道）
        if (target == null)
        {
            await DenyLinkAsync(input.Password, passwordHash: null);
        }

        if (target!.Id == currentUser.Id)
        {
            // 自关联不是枚举面（身份已知），直接拒绝无需耗时对齐
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountSelfNotAllowed);
        }

        if (target.TenantId != CurrentTenant.Id)
        {
            await DenyLinkAsync(input.Password, target.PasswordHash);
        }

        if (!target.IsActive || await _userManager.IsLockedOutAsync(target))
        {
            await DenyLinkAsync(input.Password, target.PasswordHash);
        }

        // CheckPassword 不做锁定计数，这里手动补：错误密码计入目标账号锁定（防跨账号爆破），
        // 成功后清零（与 SignInManager.CheckPasswordSignInAsync 的语义对齐）
        if (!await _userManager.CheckPasswordAsync(target, input.Password))
        {
            await _userManager.AccessFailedAsync(target);
            await DenyLinkAsync(input.Password, passwordHash: null); // CheckPassword 已含一次哈希成本，只补延迟
        }
        await _userManager.ResetAccessFailedCountAsync(target);

        var me = new IdentityLinkUserInfo(currentUser.Id, currentUser.TenantId);
        var other = new IdentityLinkUserInfo(target.Id, target.TenantId);

        var existing = await _linkUserRepository.FindAsync(me, other)
                       ?? await _linkUserRepository.FindAsync(other, me);
        if (existing != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountAlreadyLinked);
        }

        await _linkUserManager.LinkAsync(me, other);

        Logger.LogInformation("用户 {UserId} 已关联账号 {TargetUserId}", currentUser.Id, target.Id);

        // 绑定成功后单向 FindAsync 必命中（双向兜底只对上面的存在性预检有意义）
        var link = (await _linkUserRepository.FindAsync(me, other))!;

        return MapToDto(link, target);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var link = await GetLinkForCurrentUserOrThrowAsync(id);
        await _linkUserRepository.DeleteAsync(link);

        Logger.LogInformation("用户 {UserId} 已解除关联账号 {LinkId}",
            CurrentUser.GetId(), link.Id);
    }

    public virtual async Task<ImpersonationResultDto> SwitchAsync(Guid id)
    {
        var link = await GetLinkForCurrentUserOrThrowAsync(id);
        var targetUserId = link.SourceUserId == CurrentUser.GetId() ? link.TargetUserId : link.SourceUserId;

        return await _tokenExchanger.ExchangeAsync(
            targetUserId,
            AccessTokenProvider.GetAccessToken(),
            CurrentUser.FindClaimValue("client_id"),
            CurrentTenant.Id,
            GetRequestAbortedOrNone());
    }

    /// <summary>
    /// 取关联记录并强校验当前用户是其中一方（防越权操作他人关联）。
    /// </summary>
    private async Task<IdentityLinkUser> GetLinkForCurrentUserOrThrowAsync(Guid id)
    {
        var currentUserId = CurrentUser.GetId();
        var links = await _linkUserManager.GetListAsync(
            new IdentityLinkUserInfo(currentUserId, CurrentTenant.Id));

        var link = links.FirstOrDefault(l => l.Id == id);
        if (link == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountNotFound);
        }

        return link;
    }

    /// <summary>
    /// 绑定失败的统一出口：所有拒绝路径经过「防枚举延迟 + 一次哈希校验」，抹平
    /// 未知（无哈希成本）/ 停用锁定（即时抛）/ 密码错（bcrypt）之间的响应时间差——
    /// 攻击者无法用耗时区分「账号不存在 / 停用 / 存在但密码错」。
    /// passwordHash 传 null 时对占位账号验证（等量 bcrypt 成本，无任何库副作用）。
    /// </summary>
    private async Task DenyLinkAsync(string password, string? passwordHash)
    {
        await AccountAntiEnumeration.DelayAsync();

        var probe = new IdentityUser(Guid.NewGuid(), "link-probe", "link-probe@invalid.local");
        _userManager.PasswordHasher.VerifyHashedPassword(
            probe, passwordHash ?? DummyPasswordHash.Value, password);

        throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountInvalidCredentials);
    }

    /// <summary>占位哈希：进程内惰性计算一次，仅用于给「账号不存在」路径补等量哈希成本。</summary>
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new Microsoft.AspNetCore.Identity.PasswordHasher<Volo.Abp.Identity.IdentityUser>()
            .HashPassword(new Volo.Abp.Identity.IdentityUser(Guid.NewGuid(), "link-probe", "link-probe@invalid.local"),
                "Link@Probe#2026"));

    private static LinkedAccountDto MapToDto(IdentityLinkUser link, IdentityUser other)
    {
        return new LinkedAccountDto
        {
            LinkId = link.Id,
            UserId = other.Id,
            UserName = other.UserName,
            EmailAddress = other.Email ?? string.Empty,
            TenantId = other.TenantId
        };
    }
}
