using Volo.Abp.Auditing;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.OpenIddict;

public interface IOpenIddictApplicationAppService : IApplicationService
{
    Task<PagedResultDto<OpenIddictApplicationDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<OpenIddictApplicationDto> GetAsync(Guid id);

    Task<OpenIddictApplicationDto> CreateAsync(CreateOpenIddictApplicationDto input);

    Task<OpenIddictApplicationDto> UpdateAsync(Guid id, UpdateOpenIddictApplicationDto input);

    Task DeleteAsync(Guid id);

    Task<OpenIddictApplicationTokenLifetimeDto> GetTokenLifetimeAsync(Guid id);

    Task<OpenIddictApplicationTokenLifetimeDto> UpdateTokenLifetimeAsync(
        Guid id, UpdateOpenIddictApplicationTokenLifetimeDto input);

    Task<GenerateAccessTokenResultDto> GenerateAccessTokenAsync(Guid id, GenerateAccessTokenInput input);
}

public class OpenIddictApplicationDto : EntityDto<Guid>
{
    public string? ClientId { get; set; }

    public string? DisplayName { get; set; }

    public string? ClientType { get; set; }

    public string? ApplicationType { get; set; }

    public string? ConsentType { get; set; }

    public string? ClientUri { get; set; }

    public string? LogoUri { get; set; }

    public string? RedirectUris { get; set; }

    public string? PostLogoutRedirectUris { get; set; }

    public string? FrontChannelLogoutUri { get; set; }

    /* 注意：本 DTO 永远不包含 ClientSecret/JsonWebKeySet——管理 API 不返回任何凭据材料。 */

    public bool AllowAuthorizationCodeFlow { get; set; }

    public bool AllowImplicitFlow { get; set; }

    public bool AllowHybridFlow { get; set; }

    public bool AllowPasswordFlow { get; set; }

    public bool AllowClientCredentialsFlow { get; set; }

    public bool AllowRefreshTokenFlow { get; set; }

    public bool AllowTokenExchangeFlow { get; set; }

    public bool AllowDeviceAuthorizationFlow { get; set; }

    public bool EnableEndSessionEndpoint { get; set; }

    public bool EnablePushedAuthorizationEndpoint { get; set; }

    public bool RequirePkce { get; set; }

    public bool RequirePushedAuthorization { get; set; }

    /// <summary>已授予的 scope 权限（scp: 前缀已剥离）。</summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>已授予的 extension grant types（gt: 前缀已剥离，不含 8 种标准 flow）。</summary>
    public List<string> ExtensionGrantTypes { get; set; } = new();
}

public class CreateOpenIddictApplicationDto
{
    [Required]
    [StringLength(100)]
    public string ClientId { get; set; } = default!;

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = default!;

    /// <summary>public / confidential，见 OpenIddictConstants.ClientTypes。</summary>
    [Required]
    [AllowedValues(AbpAdminOpenIddictDefaults.ClientTypes.Public, AbpAdminOpenIddictDefaults.ClientTypes.Confidential)]
    public string ClientType { get; set; } = AbpAdminOpenIddictDefaults.ClientTypes.Public;

    /// <summary>web / native，见 OpenIddictConstants.ApplicationTypes。</summary>
    [Required]
    [AllowedValues(AbpAdminOpenIddictDefaults.ApplicationTypes.Web, AbpAdminOpenIddictDefaults.ApplicationTypes.Native)]
    public string ApplicationType { get; set; } = AbpAdminOpenIddictDefaults.ApplicationTypes.Web;

    /// <summary>explicit / external / implicit / systematic，见 OpenIddictConstants.ConsentTypes。</summary>
    [Required]
    [AllowedValues(
        AbpAdminOpenIddictDefaults.ConsentTypes.Explicit,
        AbpAdminOpenIddictDefaults.ConsentTypes.External,
        AbpAdminOpenIddictDefaults.ConsentTypes.Implicit,
        AbpAdminOpenIddictDefaults.ConsentTypes.Systematic)]
    public string ConsentType { get; set; } = AbpAdminOpenIddictDefaults.ConsentTypes.Implicit;

    /// <summary>只写：更新时留空表示保持原值。管理 API 永不回读该值。</summary>
    [StringLength(200)]
    [DisableAuditing]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// 只写：JWKS（JSON）。更新时 null=保持原值；空字符串=显式移除
    /// （仅在 secret 仍存在时才允许移除，见 secret 只写语义规则 4）。
    /// </summary>
    [StringLength(20000)]
    [DisableAuditing]
    public string? JsonWebKeySet { get; set; }

    [StringLength(2000)]
    public string? ClientUri { get; set; }

    [StringLength(2000)]
    public string? LogoUri { get; set; }

    /// <summary>每行一个，必须是绝对 URI。</summary>
    [StringLength(8000)]
    public string? RedirectUris { get; set; }

    /// <summary>每行一个，必须是绝对 URI。</summary>
    [StringLength(8000)]
    public string? PostLogoutRedirectUris { get; set; }

    /// <summary>必须是绝对 URI。</summary>
    [StringLength(2000)]
    public string? FrontChannelLogoutUri { get; set; }

    public bool AllowAuthorizationCodeFlow { get; set; }

    public bool AllowImplicitFlow { get; set; }

    /// <summary>勾选后服务端派生：同时启用 Authorization Code 与 Implicit。</summary>
    public bool AllowHybridFlow { get; set; }

    public bool AllowPasswordFlow { get; set; }

    public bool AllowClientCredentialsFlow { get; set; }

    public bool AllowRefreshTokenFlow { get; set; }

    public bool AllowTokenExchangeFlow { get; set; }

    public bool AllowDeviceAuthorizationFlow { get; set; }

    public bool EnableEndSessionEndpoint { get; set; }

    public bool EnablePushedAuthorizationEndpoint { get; set; }

    public bool RequirePkce { get; set; }

    /// <summary>勾选后服务端派生：同时启用 Pushed Authorization 端点。</summary>
    public bool RequirePushedAuthorization { get; set; }

    /// <summary>允许的 scopes（授予 scp: 权限）。</summary>
    public List<string>? Scopes { get; set; }

    /// <summary>extension grant types（授予 gt: 权限）。</summary>
    public List<string>? ExtensionGrantTypes { get; set; }
}

public class UpdateOpenIddictApplicationDto : CreateOpenIddictApplicationDto
{
}

/// <summary>
/// 按客户端的 token 生命周期覆盖，单位秒；null 表示移除该项覆盖、使用服务器默认值。
/// 存储在 OpenIddict application 的 Settings 字段（tkn_lft:* 键）。
/// 注意：OpenIddict 7.5.0 只支持以下 8 项——OpenIddictApplicationDescriptor 仅有 8 个
/// Set*Lifetime 方法且 OpenIddict.Server 只读取这 8 个 tkn_lft 键；
/// State token 没有应用级 lifetime 设置（descriptor / server options 均无此 API），故不在本 DTO 中。
/// </summary>
public class OpenIddictApplicationTokenLifetimeDto
{
    [Range(1, int.MaxValue)]
    public int? AccessTokenLifetime { get; set; }

    [Range(1, int.MaxValue)]
    public int? AuthorizationCodeLifetime { get; set; }

    [Range(1, int.MaxValue)]
    public int? DeviceCodeLifetime { get; set; }

    [Range(1, int.MaxValue)]
    public int? IdentityTokenLifetime { get; set; }

    [Range(1, int.MaxValue)]
    public int? RefreshTokenLifetime { get; set; }

    [Range(1, int.MaxValue)]
    public int? UserCodeLifetime { get; set; }

    /// <summary>Pushed Authorization Request 的 request token 生命周期。</summary>
    [Range(1, int.MaxValue)]
    public int? RequestTokenLifetime { get; set; }

    /// <summary>token exchange 等场景签发的「其它」token 生命周期。</summary>
    [Range(1, int.MaxValue)]
    public int? IssuedTokenLifetime { get; set; }
}

public class UpdateOpenIddictApplicationTokenLifetimeDto : OpenIddictApplicationTokenLifetimeDto
{
}

public class GenerateAccessTokenInput
{
    /// <summary>必填：已存 secret 是哈希读不回来，必须由调用者输入。</summary>
    [Required]
    [StringLength(200)]
    [DisableAuditing]
    public string ClientSecret { get; set; } = default!;

    /// <summary>请求的 scopes；每一个都必须已分配给该应用。</summary>
    public string[]? Scopes { get; set; }
}

public class GenerateAccessTokenResultDto
{
    public string AccessToken { get; set; } = default!;

    public string TokenType { get; set; } = default!;

    public int ExpiresInSeconds { get; set; }

    public string[] GrantedScopes { get; set; } = Array.Empty<string>();
}
