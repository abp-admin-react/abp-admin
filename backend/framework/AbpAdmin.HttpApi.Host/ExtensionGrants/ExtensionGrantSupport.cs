using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace AbpAdmin.ExtensionGrants;

/// <summary>
/// 三个扩展授权（Passwordless / Impersonation / LinkedAccount）的公共件
/// （审查轮从三份逐字节私有副本收拢）：错误结果、scope 解析、资源解析、
/// principal 签发管线。impersonation 保留其差异（impersonator claims +
/// IDataFilter 关过滤器建 principal、剥 offline_access）。
/// </summary>
internal static class ExtensionGrantSupport
{
    /// <summary>调用方未传 scope 时的默认集。linked-account / passwordless 保留 offline_access（真实登录会话）。</summary>
    public static readonly ImmutableArray<string> DefaultScopesWithOfflineAccess =
        new[] { "AbpAdmin", "profile", "roles", "email", "phone", "offline_access" }.ToImmutableArray();

    /// <summary>impersonation / 委托票的默认集：剥 offline_access，避免短会话被拉成可刷新长会话。</summary>
    public static readonly ImmutableArray<string> DefaultScopesWithoutOfflineAccess =
        new[] { "AbpAdmin", "profile", "roles", "email", "phone" }.ToImmutableArray();

    public static ForbidResult CreateErrorResult(string error, string? errorDescription = null)
    {
        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = errorDescription
        }!);

        return new ForbidResult(
            new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties);
    }

    /// <summary>
    /// 解析请求 scope（未传时取默认集）；stripOfflineAccess 为 true 时强制剔除
    /// offline_access（即便请求显式携带）。
    /// </summary>
    public static ImmutableArray<string> GetScopes(ExtensionGrantContext context, bool stripOfflineAccess)
    {
        var scopes = context.Request.GetScopes();
        if (stripOfflineAccess)
        {
            scopes = scopes
                .Where(scope => !string.Equals(scope, "offline_access", StringComparison.OrdinalIgnoreCase))
                .ToImmutableArray();
        }

        return scopes.Any()
            ? scopes
            : stripOfflineAccess ? DefaultScopesWithoutOfflineAccess : DefaultScopesWithOfflineAccess;
    }

    public static async Task<IEnumerable<string>> GetResourcesAsync(
        ExtensionGrantContext context,
        ImmutableArray<string> scopes)
    {
        var resources = new List<string>();
        if (!scopes.Any())
        {
            return resources;
        }

        await foreach (var resource in context.HttpContext.RequestServices
            .GetRequiredService<IOpenIddictScopeManager>()
            .ListResourcesAsync(scopes))
        {
            resources.Add(resource);
        }

        return resources;
    }

    /// <summary>
    /// principal 签发管线：SetScopes → SetResources → AbpOpenIddictClaimsPrincipalManager → SignInResult。
    /// principal 由各 grant 自行构建（claims factory / 附加 claims）。
    /// </summary>
    public static async Task<IActionResult> SignInWithScopesAsync(
        ExtensionGrantContext context,
        ClaimsPrincipal claimsPrincipal,
        bool stripOfflineAccess)
    {
        var scopes = GetScopes(context, stripOfflineAccess);
        claimsPrincipal.SetScopes(scopes);
        claimsPrincipal.SetResources(await GetResourcesAsync(context, scopes));

        await context.HttpContext.RequestServices
            .GetRequiredService<AbpOpenIddictClaimsPrincipalManager>()
            .HandleAsync(context.Request, claimsPrincipal);

        return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, claimsPrincipal);
    }
}
