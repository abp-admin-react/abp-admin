using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.OpenIddict;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Account;

/// <summary>
/// linked-account 扩展授权的 /connect/token 交换器（关联账号切换）。
/// 与 ImpersonationTokenExchanger 同构：转发当前访问令牌，grant 内复查
/// 「当前用户与目标账号存在关联」后为目标签发完整会话（含 refresh token——
/// 切换是真实登录而非模拟，会话生命周期与普通登录一致）。
/// </summary>
public class LinkedAccountTokenExchanger : ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IConnectTokenApi _connectTokenApi;

    public LinkedAccountTokenExchanger(
        IConfiguration configuration,
        IConnectTokenApi connectTokenApi)
    {
        _configuration = configuration;
        _connectTokenApi = connectTokenApi;
    }

    public virtual async Task<ImpersonationResultDto> ExchangeAsync(
        Guid targetUserId,
        string accessToken,
        string? clientId = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (accessToken.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountSwitchFailed)
                .WithData("reason", "当前请求未携带访问令牌");
        }

        var authority = _configuration["AuthServer:Authority"];
        if (authority.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountSwitchFailed)
                .WithData("reason", "AuthServer:Authority 未配置");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = AbpAdminOpenIddictDefaults.GrantTypes.LinkedAccount,
            ["client_id"] = clientId ?? "AbpAdmin_App",
            ["target_user_id"] = targetUserId.ToString()
        };

        using var response = await _connectTokenApi.ExchangeAsync(
            form,
            authorization: $"Bearer {accessToken}",
            tenantId: tenantId?.ToString(),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountSwitchFailed)
                .WithData("reason", response.Result.ReadOAuthError() ?? $"HTTP {(int)response.StatusCode}");
        }

        var payload = response.Result;
        if (payload?.AccessToken.IsNullOrWhiteSpace() != false)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.LinkedAccountSwitchFailed)
                .WithData("reason", "token 端点响应缺少 access_token");
        }

        return new ImpersonationResultDto
        {
            AccessToken = payload.AccessToken!,
            TokenType = payload.TokenType.IsNullOrWhiteSpace() ? "Bearer" : payload.TokenType!,
            ExpiresIn = payload.ExpiresIn ?? 0,
            RefreshToken = payload.RefreshToken
        };
    }
}
