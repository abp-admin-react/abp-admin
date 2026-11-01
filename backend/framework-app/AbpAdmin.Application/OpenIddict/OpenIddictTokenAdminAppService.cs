using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Authorizations;
using Volo.Abp.OpenIddict.Tokens;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 令牌/授权管理（Host 专属）。吊销=状态置 revoked（刷新/引用令牌即时失效；
/// 自包含访问令牌按 OAuth2 语义到期前仍有效）。
/// 查询走 IQueryable 组合谓词下推数据库（分页/排序/计数不载全表）——
/// OpenIddict 令牌表随登录无界增长，虽然 TokenCleanupBackgroundWorker 每小时清理过期行，
/// 活跃期内行数仍可达数万，全表载入内存分页会随规模线性恶化。
/// </summary>
[Authorize(AbpAdminPermissions.OpenIddict.Tokens.Default)]
public class OpenIddictTokenAdminAppService : AbpAdminAppService, IOpenIddictTokenAdminAppService
{
    /// <summary>Prune 阈值保留期：已兑换/吊销令牌保留 14 天供 OpenIddict 做重放检测与级联吊销（官方默认窗口）。</summary>
    private static readonly TimeSpan PruneRetention = TimeSpan.FromDays(14);

    private readonly IOpenIddictTokenRepository _tokenRepository;
    private readonly IOpenIddictAuthorizationRepository _authorizationRepository;
    private readonly IReadOnlyRepository<OpenIddictApplication, Guid> _applicationReadOnlyRepository;
    private readonly IReadOnlyRepository<OpenIddictToken, Guid> _tokenReadOnlyRepository;
    private readonly IReadOnlyRepository<OpenIddictAuthorization, Guid> _authorizationReadOnlyRepository;

    public OpenIddictTokenAdminAppService(
        IOpenIddictTokenRepository tokenRepository,
        IOpenIddictAuthorizationRepository authorizationRepository,
        IReadOnlyRepository<OpenIddictApplication, Guid> applicationReadOnlyRepository,
        IReadOnlyRepository<OpenIddictToken, Guid> tokenReadOnlyRepository,
        IReadOnlyRepository<OpenIddictAuthorization, Guid> authorizationReadOnlyRepository)
    {
        _tokenRepository = tokenRepository;
        _authorizationRepository = authorizationRepository;
        _applicationReadOnlyRepository = applicationReadOnlyRepository;
        _tokenReadOnlyRepository = tokenReadOnlyRepository;
        _authorizationReadOnlyRepository = authorizationReadOnlyRepository;
    }

    public virtual async Task<PagedResultDto<OpenIddictTokenDto>> GetTokensAsync(OpenIddictTokenListInput input)
    {
        var query = await _tokenReadOnlyRepository.GetQueryableAsync();

        // 组合谓词全部下推（Subject 与 ApplicationId 同传时同时生效，不再 if/else 丢弃其一）
        if (!string.IsNullOrWhiteSpace(input.Subject))
        {
            query = query.Where(x => x.Subject == input.Subject);
        }

        if (input.ApplicationId != null)
        {
            query = query.Where(x => x.ApplicationId == input.ApplicationId);
        }

        if (!string.IsNullOrWhiteSpace(input.Type))
        {
            query = query.Where(x => x.Type == input.Type);
        }

        if (!string.IsNullOrWhiteSpace(input.Status))
        {
            query = query.Where(x => x.Status == input.Status);
        }

        // 固定按创建时间倒序（令牌表运维视角）；输入 DTO 不暴露 Sorting，契约与行为一致
        return await QueryPagedResultAsync(
            query.OrderByDescending(x => x.CreationDate), input, MapTokensAsync);
    }

    public virtual async Task<PagedResultDto<OpenIddictAuthorizationDto>> GetAuthorizationsAsync(OpenIddictAuthorizationListInput input)
    {
        var query = await _authorizationReadOnlyRepository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Subject))
        {
            query = query.Where(x => x.Subject == input.Subject);
        }

        if (input.ApplicationId != null)
        {
            query = query.Where(x => x.ApplicationId == input.ApplicationId);
        }

        if (!string.IsNullOrWhiteSpace(input.Status))
        {
            query = query.Where(x => x.Status == input.Status);
        }

        return await QueryPagedResultAsync(
            query.OrderByDescending(x => x.CreationDate), input, MapAuthorizationsAsync);
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Tokens.Revoke)]
    [OperationLog("令牌管理", "吊销令牌", BizNo = "{{id}}", Success = "吊销了令牌 {{id}}")]
    public virtual async Task RevokeTokenAsync(Guid id)
    {
        var token = await _tokenRepository.GetAsync(id);
        if (token.Status != OpenIddictConstants.Statuses.Revoked)
        {
            token.Status = OpenIddictConstants.Statuses.Revoked;
            await _tokenRepository.UpdateAsync(token, autoSave: true);
        }
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Tokens.Revoke)]
    [OperationLog("令牌管理", "吊销授权", BizNo = "{{id}}", Success = "吊销了授权 {{id}}")]
    public virtual async Task RevokeAuthorizationAsync(Guid id)
    {
        var authorization = await _authorizationRepository.GetAsync(id);
        if (authorization.Status != OpenIddictConstants.Statuses.Revoked)
        {
            authorization.Status = OpenIddictConstants.Statuses.Revoked;
            await _authorizationRepository.UpdateAsync(authorization, autoSave: true);
        }

        // 授权下挂的令牌一并吊销
        await _tokenRepository.RevokeByAuthorizationIdAsync(id);
    }

    /// <summary>吊销某用户全部令牌与授权（等效强制其在所有客户端重新登录并重新授权）。</summary>
    [Authorize(AbpAdminPermissions.OpenIddict.Tokens.Revoke)]
    [OperationLog("令牌管理", "按用户吊销", Success = "吊销了用户 {{user(subject)}} 的全部令牌与授权")]
    public virtual async Task<int> RevokeBySubjectAsync(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.SubjectRequired);
        }

        var tokenCount = (int)await _tokenRepository.RevokeBySubjectAsync(subject);

        // 与 RevokeAuthorizationAsync 对称：授权行也置 revoked，否则"按用户吊销"后
        // 授权列表仍显示有效，凭有效授权发新令牌的流程不会被阻断
        await _authorizationRepository.RevokeBySubjectAsync(subject);

        return tokenCount;
    }

    /// <summary>
    /// 手动清理过期令牌/授权。阈值=当前时间减 14 天保留期：刚兑换的 refresh token
    /// 依赖保留窗口做重放检测与级联吊销，立即清理会丢掉这层信号。
    /// （后台 TokenCleanupBackgroundWorker 用同一 OpenIddict 语义周期清理，本方法用于即时运维。）
    /// </summary>
    [Authorize(AbpAdminPermissions.OpenIddict.Tokens.Prune)]
    public virtual Task<OpenIddictPruneResultDto> PruneAsync()
    {
        var threshold = DateTime.UtcNow - PruneRetention;
        return PruneAsync(threshold);
    }

    protected virtual async Task<OpenIddictPruneResultDto> PruneAsync(DateTime threshold)
    {
        return new OpenIddictPruneResultDto
        {
            PrunedTokens = await _tokenRepository.PruneAsync(threshold),
            PrunedAuthorizations = await _authorizationRepository.PruneAsync(threshold)
        };
    }

    /// <summary>
    /// 分页查询骨架（GetTokens/GetAuthorizations 共用）：计数 + 分页下推数据库，
    /// 当前页实体交给 mapPage 组装 DTO（内部批量补 ClientId，避免逐行 FindAsync 的 N+1）。
    /// </summary>
    private async Task<PagedResultDto<TDto>> QueryPagedResultAsync<TEntity, TDto>(
        IQueryable<TEntity> orderedQuery, PagedResultRequestDto input,
        Func<List<TEntity>, Task<List<TDto>>> mapPage)
    {
        var total = await AsyncExecuter.CountAsync(orderedQuery);
        var page = await AsyncExecuter.ToListAsync(
            orderedQuery.Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<TDto>(total, await mapPage(page));
    }

    private async Task<List<OpenIddictTokenDto>> MapTokensAsync(List<OpenIddictToken> page)
    {
        var appClientIds = await LoadApplicationClientIdsAsync(page.Select(x => x.ApplicationId).ToList());
        return page.Select(x => new OpenIddictTokenDto
        {
            Id = x.Id,
            ApplicationId = x.ApplicationId,
            ApplicationClientId = x.ApplicationId != null && appClientIds.TryGetValue(x.ApplicationId.Value, out var clientId) ? clientId : null,
            AuthorizationId = x.AuthorizationId,
            Subject = x.Subject,
            ReferenceId = x.ReferenceId,
            Status = x.Status,
            Type = x.Type,
            CreationDate = x.CreationDate,
            ExpirationDate = x.ExpirationDate,
            RedemptionDate = x.RedemptionDate
        }).ToList();
    }

    private async Task<List<OpenIddictAuthorizationDto>> MapAuthorizationsAsync(List<OpenIddictAuthorization> page)
    {
        var appClientIds = await LoadApplicationClientIdsAsync(page.Select(x => x.ApplicationId).ToList());
        return page.Select(x => new OpenIddictAuthorizationDto
        {
            Id = x.Id,
            ApplicationId = x.ApplicationId,
            ApplicationClientId = x.ApplicationId != null && appClientIds.TryGetValue(x.ApplicationId.Value, out var clientId) ? clientId : null,
            Subject = x.Subject,
            Status = x.Status,
            Type = x.Type,
            Scopes = x.Scopes,
            CreationDate = x.CreationDate
        }).ToList();
    }

    /// <summary>按应用 Id 批量取 ClientId（一次查询，避免逐行 FindAsync 的 N+1）。</summary>
    private async Task<Dictionary<Guid, string>> LoadApplicationClientIdsAsync(List<Guid?> applicationIds)
    {
        var ids = applicationIds.Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var apps = await _applicationReadOnlyRepository.GetListAsync(x => ids.Contains(x.Id));
        return apps.ToDictionary(x => x.Id, x => x.ClientId ?? string.Empty);
    }
}
