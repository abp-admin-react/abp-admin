using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 走标准 /connect/token 端点交换 token（不在进程内调 OpenIddict 内部 API 造 token）。
/// HTTP 调用走 IConnectTokenApi 声明式客户端（HttpAgent），底层仍是
/// AbpAdminApplicationModule 注册的 "AuthServerTokenExchange" 命名 HttpClient
/// （开发环境 AuthServer 是自签 dev-cert，该 client 已跳过 SSL 校验）。
/// </summary>
public class OpenIddictTokenExchanger : IOpenIddictTokenExchanger, ITransientDependency
{
    private readonly IConnectTokenApi _connectTokenApi;
    private readonly IConfiguration _configuration;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    public OpenIddictTokenExchanger(
        IConnectTokenApi connectTokenApi,
        IConfiguration configuration,
        IStringLocalizer<AbpAdminResource> localizer)
    {
        _connectTokenApi = connectTokenApi;
        _configuration = configuration;
        _localizer = localizer;
    }

    public virtual async Task<GenerateAccessTokenResultDto> ExchangeAsync(
        string clientId, string clientSecret, string[] scopes)
    {
        var authority = _configuration["AuthServer:Authority"];
        if (authority.IsNullOrWhiteSpace())
        {
            // reason 走本地化（zh/en 均有文案），不再硬编码中文
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenFailed)
                .WithData("reason", _localizer[AbpAdminDomainErrorCodes.Account.OpenIddictAuthorityNotConfigured].Value);
        }

        using var response = await _connectTokenApi.ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = string.Join(' ', scopes)
        });

        if (!response.IsSuccessStatusCode)
        {
            // 标准 OAuth 错误响应 {"error":"invalid_client","error_description":"..."}（secret 错误等）
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenFailed)
                .WithData("reason", response.Result.ReadOAuthError() ?? $"HTTP {(int)response.StatusCode}");
        }

        var payload = response.Result;
        if (payload?.AccessToken.IsNullOrWhiteSpace() != false)
        {
            // 2xx 但缺 access_token 的畸形响应（原版 GetProperty 在此抛 KeyNotFoundException，改为显式业务错误）
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.GenerateAccessTokenFailed)
                .WithData("reason", "token 端点响应缺少 access_token");
        }

        return new GenerateAccessTokenResultDto
        {
            AccessToken = payload.AccessToken!,
            TokenType = payload.TokenType.IsNullOrWhiteSpace() ? "Bearer" : payload.TokenType!,
            ExpiresInSeconds = payload.ExpiresIn ?? 0,
            GrantedScopes = payload.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>()
        };
    }
}
