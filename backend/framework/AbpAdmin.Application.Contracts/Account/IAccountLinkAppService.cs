using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace AbpAdmin.Account;

/// <summary>
/// Linked Accounts（关联账号）：一个用户绑定多个同租户账号并在其间切换（对标 ABP Pro，v1 限同租户）。
/// 关联需要目标账号密码确认（证明本人）；切换走 linked-account 扩展授权换取目标账号完整会话。
/// </summary>
public interface IAccountLinkAppService : IApplicationService
{
    /// <summary>当前用户的关联账号列表（每个关联返回对端账号信息）。</summary>
    Task<IReadOnlyList<LinkedAccountDto>> GetListAsync();

    /// <summary>
    /// 绑定另一个账号：需提供对方用户名/邮箱 + 密码（防爆破锁定计入对方账号）。
    /// </summary>
    Task<LinkedAccountDto> LinkAsync(LinkAccountInput input);

    /// <summary>解除关联（当前用户必须是关联的一方）。方法名 Delete 前缀承接 ABP 约定动词 DELETE /{id}。</summary>
    Task DeleteAsync(Guid id);

    /// <summary>切换到关联账号：返回目标账号的完整令牌（含 refresh token，是真实登录而非模拟）。</summary>
    Task<ImpersonationResultDto> SwitchAsync(Guid id);
}

public class LinkedAccountDto
{
    /// <summary>关联记录 Id（UnlinkAsync / SwitchAsync 的入参）。</summary>
    public Guid LinkId { get; set; }

    /// <summary>对端账号 Id。</summary>
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public Guid? TenantId { get; set; }
}

public class LinkAccountInput
{
    [Required]
    public string UserNameOrEmail { get; set; } = string.Empty;

    [Required]
    [DisableAuditing]
    public string Password { get; set; } = string.Empty;
}
