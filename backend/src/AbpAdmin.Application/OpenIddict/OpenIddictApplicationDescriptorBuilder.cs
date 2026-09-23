using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.OpenIddict.Applications;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// T2.9：把 Create/Update DTO 应用到 <see cref="AbpApplicationDescriptor"/> 的唯一入口。
/// 旧实现把一组权限写死、且更新时 Permissions 只 Add 不 Clear（只增不减），
/// 这里一律 <see cref="HashSet{T}.Clear"/> 后按入参重建。
///
/// 两条派生规则在服务端实现（直接调 API 也保证一致，前端只是镜像成 UI 联动）：
/// 1. 启用 Hybrid → 同时启用 Authorization Code 和 Implicit（grant/response type/endpoint 全套）；
/// 2. 强制 PAR（require pushed authorization requests）→ 同时启用 Pushed Authorization 端点。
/// </summary>
public static class OpenIddictApplicationDescriptorBuilder
{
    /// <summary>8 种标准 flow 的 grant type 权限，用于从存储权限中反解 extension grant types。</summary>
    public static readonly ImmutableArray<string> StandardGrantTypePermissions =
    [
        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.GrantTypes.Implicit,
        OpenIddictConstants.Permissions.GrantTypes.Password,
        OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.GrantTypes.TokenExchange,
        OpenIddictConstants.Permissions.GrantTypes.DeviceCode
    ];

    /// <summary>新建：从零构建 descriptor（含凭据校验）。</summary>
    public static AbpApplicationDescriptor Build(CreateOpenIddictApplicationDto input)
    {
        var descriptor = new AbpApplicationDescriptor();
        Apply(descriptor, input);
        return descriptor;
    }

    /// <summary>
    /// 把表单字段应用到 descriptor 上。新建时传入新实例；更新时传入
    /// 已用 <c>PopulateAsync(descriptor, entity)</c> 填充过的实例
    /// （此时 descriptor.ClientSecret 是已存哈希，留空保持语义依赖这一点）。
    /// </summary>
    public static void Apply(AbpApplicationDescriptor descriptor, CreateOpenIddictApplicationDto input)
    {
        descriptor.ClientId = input.ClientId;
        descriptor.DisplayName = input.DisplayName;
        descriptor.ClientType = input.ClientType;
        descriptor.ApplicationType = input.ApplicationType;
        descriptor.ConsentType = input.ConsentType;
        descriptor.ClientUri = input.ClientUri;
        descriptor.LogoUri = input.LogoUri;
        descriptor.FrontChannelLogoutUri = ParseAbsoluteUri(input.FrontChannelLogoutUri, nameof(input.FrontChannelLogoutUri));

        descriptor.RedirectUris.Clear();
        foreach (var value in OpenIddictTextUtils.SplitList(input.RedirectUris))
        {
            descriptor.RedirectUris.Add(ParseAbsoluteUri(value, nameof(input.RedirectUris))!);
        }

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var value in OpenIddictTextUtils.SplitList(input.PostLogoutRedirectUris))
        {
            descriptor.PostLogoutRedirectUris.Add(ParseAbsoluteUri(value, nameof(input.PostLogoutRedirectUris))!);
        }

        ApplyCredentials(descriptor, input);
        ApplyFlowsEndpointsAndRequirements(descriptor, input);
    }

    /// <summary>
    /// client secret 只写语义的组合规则（规则 3/4/5 的判定交织在一起，不直观，集中在这一处）：
    /// 规则 1（管理 API 永不返回 secret 明文/哈希）由输出 DTO 不含凭据字段保证；
    /// 规则 2：secret 输入 null 或纯空白=保持原值；显式空字符串=显式清空；
    /// 规则 3：把应用切成 Public 时，同时移除其已存 secret 与 JWKS；
    /// 规则 4：JWKS 输入 null=保持原值；空/空白字符串=显式移除——secret 与 JWKS 的显式移除
    ///         是对称的：仅当另一种凭据仍存在时才允许，不能把机密客户端的最后一种凭据也移除掉
    ///        （「只有 secret 的应用清空 secret 并保存」也会被拒）；
    /// 规则 5：Public 客户端不能持有 secret 或 JWKS；Confidential 客户端至少要有一种凭据（二选一）。
    /// </summary>
    private static void ApplyCredentials(AbpApplicationDescriptor descriptor, CreateOpenIddictApplicationDto input)
    {
        if (string.Equals(input.ClientType, OpenIddictConstants.ClientTypes.Public, StringComparison.OrdinalIgnoreCase))
        {
            // 规则 5a：Public 显式携带凭据 → 直接拒绝（早于规则 3 的静默清除，避免调用方误以为凭据生效）
            if (!input.ClientSecret.IsNullOrWhiteSpace() || !input.JsonWebKeySet.IsNullOrWhiteSpace())
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.PublicClientCannotHaveCredentials);
            }

            // 规则 3：切成 Public → 移除已存 secret 与 JWKS（新建时本来就是 null，幂等）
            descriptor.ClientSecret = null;
            descriptor.JsonWebKeySet = null;
            return;
        }

        // 规则 2：secret 为 null/纯空白 → 保持原值（更新时 descriptor.ClientSecret 已被
        // Populate 填成已存哈希；新建时是 null，保持即无 secret）。显式空字符串=显式清空。
        if (!input.ClientSecret.IsNullOrWhiteSpace())
        {
            descriptor.ClientSecret = input.ClientSecret;
        }
        else if (input.ClientSecret is not null && input.ClientSecret.Length == 0)
        {
            // 规则 4（对称）：显式清空 secret 仅当 JWKS 仍存在时允许
            if (descriptor.JsonWebKeySet?.Keys is not { Count: > 0 })
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.CannotRemoveLastCredential);
            }

            descriptor.ClientSecret = null;
        }

        // JWKS：null=保持原值；空/空白字符串=显式移除；非空=替换
        if (input.JsonWebKeySet != null)
        {
            if (input.JsonWebKeySet.IsNullOrWhiteSpace())
            {
                // 规则 4：空 JWKS 仅在另一种凭据（secret）仍存在时才显式移除
                if (descriptor.ClientSecret.IsNullOrEmpty())
                {
                    throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.CannotRemoveLastCredential);
                }

                descriptor.JsonWebKeySet = null;
            }
            else
            {
                descriptor.JsonWebKeySet = ParseJsonWebKeySet(input.JsonWebKeySet);
            }
        }

        // 规则 5b：Confidential 至少要有一种凭据（OpenIddict 校验 ID2113/ID2114 也会拦，
        // 但抛的是 ValidationException；这里先抛中文业务异常）
        if (descriptor.ClientSecret.IsNullOrEmpty() && descriptor.JsonWebKeySet?.Keys is not { Count: > 0 })
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.ConfidentialClientRequiresCredential);
        }
    }

    private static void ApplyFlowsEndpointsAndRequirements(
        AbpApplicationDescriptor descriptor, CreateOpenIddictApplicationDto input)
    {
        // 权限不再只增不减的关键：Populate 之后一律 Clear 再按入参重建
        descriptor.Permissions.Clear();
        descriptor.Requirements.Clear();

        // 派生规则 1：启用 Hybrid → 同时启用 Authorization Code 和 Implicit
        var allowAuthorizationCode = input.AllowAuthorizationCodeFlow || input.AllowHybridFlow;
        var allowImplicit = input.AllowImplicitFlow || input.AllowHybridFlow;

        if (allowAuthorizationCode)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (allowImplicit)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Implicit);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdToken);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdTokenToken);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Token);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        }

        if (input.AllowHybridFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeToken);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdTokenToken);
        }

        if (input.AllowPasswordFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.AllowClientCredentialsFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.AllowRefreshTokenFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.AllowTokenExchangeFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.TokenExchange);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.AllowDeviceAuthorizationFlow)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.DeviceCode);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.DeviceAuthorization);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        }

        if (input.EnableEndSessionEndpoint)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.EndSession);
        }

        // 派生规则 2：强制 PAR → 启用 Pushed Authorization 端点
        if (input.EnablePushedAuthorizationEndpoint || input.RequirePushedAuthorization)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.PushedAuthorization);
        }

        if (input.RequirePkce)
        {
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        }

        if (input.RequirePushedAuthorization)
        {
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.PushedAuthorizationRequests);
        }

        // 允许的 scopes（scp: 权限，含内置 scope 与托管 scope，重名由 HashSet 去重）
        foreach (var scope in (input.Scopes ?? new List<string>()).Where(x => !x.IsNullOrWhiteSpace()).Distinct())
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope.Trim());
        }

        // extension grant types（gt: 权限）
        foreach (var grantType in (input.ExtensionGrantTypes ?? new List<string>()).Where(x => !x.IsNullOrWhiteSpace()).Distinct())
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.GrantType + grantType.Trim());
        }
    }

    /// <summary>
    /// 把 8 项 token lifetime 覆盖写入 descriptor.Settings（单位秒，null=移除该项覆盖）。
    /// 注意：OpenIddict 7.5.0 的 descriptor 只有 8 个 Set*Lifetime 方法（无 Get*、无 State token），
    /// OpenIddict.Server 也只读取这 8 个 tkn_lft:* 键；State token 没有应用级 lifetime 设置可写。
    /// </summary>
    public static void ApplyTokenLifetimes(
        OpenIddictApplicationDescriptor descriptor, OpenIddictApplicationTokenLifetimeDto input)
    {
        descriptor.SetAccessTokenLifetime(ToTimeSpan(input.AccessTokenLifetime));
        descriptor.SetAuthorizationCodeLifetime(ToTimeSpan(input.AuthorizationCodeLifetime));
        descriptor.SetDeviceCodeLifetime(ToTimeSpan(input.DeviceCodeLifetime));
        descriptor.SetIdentityTokenLifetime(ToTimeSpan(input.IdentityTokenLifetime));
        descriptor.SetRefreshTokenLifetime(ToTimeSpan(input.RefreshTokenLifetime));
        descriptor.SetUserCodeLifetime(ToTimeSpan(input.UserCodeLifetime));
        descriptor.SetRequestTokenLifetime(ToTimeSpan(input.RequestTokenLifetime));
        descriptor.SetIssuedTokenLifetime(ToTimeSpan(input.IssuedTokenLifetime));
    }

    /// <summary>
    /// 从 GetSettingsAsync 读回 8 项覆盖。descriptor 没有 Get*Lifetime 方法，
    /// 用官方常量 OpenIddictConstants.Settings.TokenLifetimes.* 读字典（不是手写 JSON key）。
    /// </summary>
    public static OpenIddictApplicationTokenLifetimeDto ToTokenLifetimeDto(
        IReadOnlyDictionary<string, string> settings)
    {
        return new OpenIddictApplicationTokenLifetimeDto
        {
            AccessTokenLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.AccessToken),
            AuthorizationCodeLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.AuthorizationCode),
            DeviceCodeLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.DeviceCode),
            IdentityTokenLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.IdentityToken),
            RefreshTokenLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.RefreshToken),
            UserCodeLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.UserCode),
            RequestTokenLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.RequestToken),
            IssuedTokenLifetime = GetSeconds(settings, OpenIddictConstants.Settings.TokenLifetimes.IssuedToken)
        };
    }

    private static TimeSpan? ToTimeSpan(int? seconds)
    {
        return seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : null;
    }

    private static int? GetSeconds(IReadOnlyDictionary<string, string> settings, string key)
    {
        if (settings.TryGetValue(key, out var value) &&
            TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var lifetime))
        {
            return (int)lifetime.TotalSeconds;
        }

        return null;
    }

    private static Uri? ParseAbsoluteUri(string? value, string fieldName)
    {
        if (value.IsNullOrWhiteSpace())
        {
            return null;
        }

        // 先校验再抛中文业务异常，而不是让 new Uri 抛 UriFormatException 变成 500
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.InvalidAbsoluteUri)
                .WithData("field", fieldName)
                .WithData("value", value);
        }

        return uri;
    }

    private static JsonWebKeySet ParseJsonWebKeySet(string json)
    {
        try
        {
            return new JsonWebKeySet(json);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.InvalidJsonWebKeySet)
                .WithData("field", "JsonWebKeySet")
                .WithData("value", exception.Message);
        }
    }
}
