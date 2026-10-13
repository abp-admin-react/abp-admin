using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.OpenIddict.Applications;

namespace AbpAdmin.OpenIddict;

[Authorize(AbpAdminPermissions.OpenIddict.Applications.Default)]
public class OpenIddictApplicationAppService : AbpAdminAppService, IOpenIddictApplicationAppService
{
    private readonly IOpenIddictApplicationRepository _applicationRepository;
    private readonly IAbpApplicationManager _applicationManager;
    private readonly IOpenIddictTokenExchanger _tokenExchanger;

    public OpenIddictApplicationAppService(
        IOpenIddictApplicationRepository applicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictTokenExchanger tokenExchanger)
    {
        _applicationRepository = applicationRepository;
        _applicationManager = applicationManager;
        _tokenExchanger = tokenExchanger;
    }

    public virtual async Task<PagedResultDto<OpenIddictApplicationDto>> GetListAsync(
        PagedAndSortedResultRequestDto input)
    {
        var count = await _applicationRepository.GetCountAsync();
        var items = await _applicationRepository.GetListAsync(
            input.Sorting,
            input.SkipCount,
            input.MaxResultCount);
        return new PagedResultDto<OpenIddictApplicationDto>(count, items.Select(Map).ToList());
    }

    public virtual async Task<OpenIddictApplicationDto> GetAsync(Guid id)
    {
        return Map(await _applicationRepository.GetAsync(id));
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Applications.Create)]
    public virtual async Task<OpenIddictApplicationDto> CreateAsync(CreateOpenIddictApplicationDto input)
    {
        var descriptor = OpenIddictApplicationDescriptorBuilder.Build(input);
        await _applicationManager.CreateAsync(descriptor);
        var entity = await _applicationRepository.FindByClientIdAsync(input.ClientId)
            ?? throw new EntityNotFoundException(typeof(OpenIddictApplication), input.ClientId);
        return Map(entity);
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Applications.Update)]
    public virtual async Task<OpenIddictApplicationDto> UpdateAsync(Guid id, UpdateOpenIddictApplicationDto input)
    {
        var application = await FindApplicationModelAsync(id);
        // 必须用 AbpApplicationDescriptor，FrontChannelLogoutUri/ClientUri/LogoUri 才会被 ABP 存下来
        var descriptor = new AbpApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);
        OpenIddictApplicationDescriptorBuilder.Apply(descriptor, input);
        await _applicationManager.UpdateAsync(application, descriptor);
        return Map(await _applicationRepository.GetAsync(id));
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Applications.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var application = await FindApplicationModelAsync(id);
        await _applicationManager.DeleteAsync(application);
    }

    public virtual async Task<OpenIddictApplicationTokenLifetimeDto> GetTokenLifetimeAsync(Guid id)
    {
        var application = await FindApplicationModelAsync(id);
        var settings = await _applicationManager.GetSettingsAsync(application);
        return OpenIddictApplicationDescriptorBuilder.ToTokenLifetimeDto(settings);
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Applications.Update)]
    public virtual async Task<OpenIddictApplicationTokenLifetimeDto> UpdateTokenLifetimeAsync(
        Guid id, UpdateOpenIddictApplicationTokenLifetimeDto input)
    {
        var application = await FindApplicationModelAsync(id);
        // lifetime 覆盖存在 Settings 字段（不是 Properties）；走 Populate→Set*Lifetime→UpdateAsync，
        // 等价于 GetSettingsAsync/SetSettingsAsync 的效果且不碰其它字段（含已存 secret 哈希）。
        var descriptor = new AbpApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);
        OpenIddictApplicationDescriptorBuilder.ApplyTokenLifetimes(descriptor, input);
        await _applicationManager.UpdateAsync(application, descriptor);

        var settings = await _applicationManager.GetSettingsAsync(application);
        return OpenIddictApplicationDescriptorBuilder.ToTokenLifetimeDto(settings);
    }

    /// <summary>
    /// ABP 的 AbpApplicationManager 泛型实参是 OpenIddictApplicationModel（不是 EF 实体），
    /// 所有 manager 写路径都必须先经 FindByIdAsync 拿到 Model，
    /// 否则 Populate/Update/Delete 内部 cast 会抛 InvalidCastException。
    /// </summary>
    private async Task<object> FindApplicationModelAsync(Guid id)
    {
        var application = await _applicationManager.FindByIdAsync(id.ToString());
        if (application is null)
        {
            throw new EntityNotFoundException(typeof(OpenIddictApplication), id);
        }

        return application;
    }

    /// <summary>
    /// Client Credentials 代取 access token。四个条件前后端都判（缺一即拒）：
    /// 1. 当前用户有 GenerateAccessToken 权限（[Authorize] 保证）；
    /// 2. 应用是 Confidential；3. 已启用 Client Credentials flow；4. 请求的 scope 全部分配。
    /// 已存 secret 是哈希读不回来，所以 secret 必须由调用者输入；
    /// HTTP 交换交给 <see cref="IOpenIddictTokenExchanger"/>（走标准 /connect/token 端点）。
    /// </summary>
    [Authorize(AbpAdminPermissions.OpenIddict.Applications.GenerateAccessToken)]
    public virtual async Task<GenerateAccessTokenResultDto> GenerateAccessTokenAsync(
        Guid id, GenerateAccessTokenInput input)
    {
        var entity = await _applicationRepository.GetAsync(id);
        var requestedScopes = (input.Scopes ?? Array.Empty<string>())
            .Where(x => !x.IsNullOrWhiteSpace())
            .Select(x => x.Trim())
            .Distinct()
            .ToArray();

        EnsureClientCredentialsAllowed(entity, OpenIddictTextUtils.ParseJsonArray(entity.Permissions), requestedScopes);

        return await _tokenExchanger.ExchangeAsync(entity.ClientId!, input.ClientSecret, requestedScopes);
    }

    /// <summary>条件 2/3/4 的业务校验（异常类型与 WithData 载荷与拆分前保持一致）。</summary>
    private static void EnsureClientCredentialsAllowed(
        OpenIddictApplication entity, List<string> permissions, string[] requestedScopes)
    {
        if (!string.Equals(entity.ClientType, OpenIddictConstants.ClientTypes.Confidential,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenNotAllowed);
        }

        if (!permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenNotAllowed);
        }

        var assignedScopes = GetAssignedScopes(permissions);
        foreach (var scope in requestedScopes)
        {
            if (!assignedScopes.Contains(scope))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.ScopeNotAssigned)
                    .WithData("scope", scope);
            }
        }
    }

    /// <summary>从 Permissions 里反解 scp: 前缀的已分配 scopes（与 Map 的 Scopes 字段同一口径）。</summary>
    private static List<string> GetAssignedScopes(List<string> permissions)
    {
        var scopePrefix = OpenIddictConstants.Permissions.Prefixes.Scope;
        return permissions
            .Where(x => x.StartsWith(scopePrefix, StringComparison.Ordinal))
            .Select(x => x[scopePrefix.Length..])
            .ToList();
    }

    private static OpenIddictApplicationDto Map(OpenIddictApplication item)
    {
        var permissions = OpenIddictTextUtils.ParseJsonArray(item.Permissions);
        var requirements = OpenIddictTextUtils.ParseJsonArray(item.Requirements);

        var allowAuthorizationCode =
            permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        var allowImplicit =
            permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.Implicit);

        var scopePrefix = OpenIddictConstants.Permissions.Prefixes.Scope;
        var grantTypePrefix = OpenIddictConstants.Permissions.Prefixes.GrantType;

        return new OpenIddictApplicationDto
        {
            Id = item.Id,
            ClientId = item.ClientId,
            DisplayName = item.DisplayName,
            ClientType = item.ClientType,
            ApplicationType = item.ApplicationType,
            ConsentType = item.ConsentType,
            ClientUri = item.ClientUri,
            LogoUri = item.LogoUri,
            RedirectUris = item.RedirectUris,
            PostLogoutRedirectUris = item.PostLogoutRedirectUris,
            FrontChannelLogoutUri = item.FrontChannelLogoutUri,
            AllowAuthorizationCodeFlow = allowAuthorizationCode,
            AllowImplicitFlow = allowImplicit,
            // hybrid 写过 rst:code id_token 等组合 response type，用它反解，
            // 与「单独勾 AuthCode+Implicit」区分开
            AllowHybridFlow = allowAuthorizationCode && allowImplicit &&
                (permissions.Contains(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken) ||
                 permissions.Contains(OpenIddictConstants.Permissions.ResponseTypes.CodeToken) ||
                 permissions.Contains(OpenIddictConstants.Permissions.ResponseTypes.CodeIdTokenToken)),
            AllowPasswordFlow =
                permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.Password),
            AllowClientCredentialsFlow =
                permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials),
            AllowRefreshTokenFlow =
                permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.RefreshToken),
            AllowTokenExchangeFlow =
                permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.TokenExchange),
            AllowDeviceAuthorizationFlow =
                permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.DeviceCode),
            EnableEndSessionEndpoint =
                permissions.Contains(OpenIddictConstants.Permissions.Endpoints.EndSession),
            EnablePushedAuthorizationEndpoint =
                permissions.Contains(OpenIddictConstants.Permissions.Endpoints.PushedAuthorization),
            RequirePkce =
                requirements.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange),
            RequirePushedAuthorization =
                requirements.Contains(OpenIddictConstants.Requirements.Features.PushedAuthorizationRequests),
            Scopes = permissions
                .Where(x => x.StartsWith(scopePrefix, StringComparison.Ordinal))
                .Select(x => x[scopePrefix.Length..])
                .ToList(),
            ExtensionGrantTypes = permissions
                .Where(x => x.StartsWith(grantTypePrefix, StringComparison.Ordinal) &&
                    !OpenIddictApplicationDescriptorBuilder.StandardGrantTypePermissions.Contains(x))
                .Select(x => x[grantTypePrefix.Length..])
                .ToList()
        };
    }
}
