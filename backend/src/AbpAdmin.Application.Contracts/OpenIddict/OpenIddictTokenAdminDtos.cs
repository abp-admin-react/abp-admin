using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 令牌/授权管理（借鉴芋道"令牌管理"）。Host 专属：OpenIddict 模块不区分租户，
/// 令牌与授权都存宿主库。吊销语义：把状态置为 revoked——刷新令牌立刻失效；
/// 引用令牌（reference token）即时失效，自包含访问令牌到期前仍有效（OAuth2 标准行为）。
/// </summary>
public interface IOpenIddictTokenAdminAppService : IApplicationService
{
    Task<PagedResultDto<OpenIddictTokenDto>> GetTokensAsync(OpenIddictTokenListInput input);

    Task<PagedResultDto<OpenIddictAuthorizationDto>> GetAuthorizationsAsync(OpenIddictAuthorizationListInput input);

    /// <summary>吊销单个令牌（状态置 revoked）。</summary>
    Task RevokeTokenAsync(Guid id);

    /// <summary>吊销单个授权，并连带吊销其下全部令牌。</summary>
    Task RevokeAuthorizationAsync(Guid id);

    /// <summary>吊销某用户的全部令牌（等效强制该用户在所有应用重新登录）。</summary>
    Task<int> RevokeBySubjectAsync(string subject);

    /// <summary>手动清理过期令牌与授权，返回清理数量。</summary>
    Task<OpenIddictPruneResultDto> PruneAsync();
}

/// <summary>令牌列表输入。不继承 PagedAndSortedResultRequestDto：实现固定按创建时间倒序，不暴露 Sorting（契约与行为一致）。</summary>
public class OpenIddictTokenListInput : PagedResultRequestDto
{
    /// <summary>用户 Id（subject）。</summary>
    public string? Subject { get; set; }

    /// <summary>应用 Id。</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>令牌类型：access_token / refresh_token / ...；空=全部。</summary>
    public string? Type { get; set; }

    /// <summary>状态：valid / revoked / redeemed / expired；空=全部。</summary>
    public string? Status { get; set; }
}

/// <summary>授权列表输入。同上：固定按创建时间倒序，不暴露 Sorting。</summary>
public class OpenIddictAuthorizationListInput : PagedResultRequestDto
{
    public string? Subject { get; set; }

    public Guid? ApplicationId { get; set; }

    public string? Status { get; set; }
}

public class OpenIddictTokenDto : EntityDto<Guid>
{
    public Guid? ApplicationId { get; set; }

    /// <summary>应用 ClientId（冗余便于展示）。</summary>
    public string? ApplicationClientId { get; set; }

    public Guid? AuthorizationId { get; set; }

    public string? Subject { get; set; }

    public string? ReferenceId { get; set; }

    public string? Status { get; set; }

    public string? Type { get; set; }

    public DateTime? CreationDate { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public DateTime? RedemptionDate { get; set; }
}

public class OpenIddictAuthorizationDto : EntityDto<Guid>
{
    public Guid? ApplicationId { get; set; }

    public string? ApplicationClientId { get; set; }

    public string? Subject { get; set; }

    public string? Status { get; set; }

    public string? Type { get; set; }

    /// <summary>授权的 scopes，逗号分隔。</summary>
    public string? Scopes { get; set; }

    public DateTime? CreationDate { get; set; }
}

public class OpenIddictPruneResultDto
{
    public long PrunedTokens { get; set; }

    public long PrunedAuthorizations { get; set; }
}
