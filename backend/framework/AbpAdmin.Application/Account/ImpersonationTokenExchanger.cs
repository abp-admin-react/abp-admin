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
/// impersonation 扩展授权的 /connect/token 交换器（重构报告问题 8：从 AccountProAppService 抽出）。
/// HTTP 调用走 IConnectTokenApi 声明式客户端（与 OpenIddictTokenExchanger 共用，form 组装与
/// OAuth 错误解析的重复样板已收拢到该接口与 ReadOAuthError），供模拟登录各入口复用。
/// </summary>
public class ImpersonationTokenExchanger : ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IConnectTokenApi _connectTokenApi;

    public ImpersonationTokenExchanger(
        IConfiguration configuration,
        IConnectTokenApi connectTokenApi)
    {
        _configuration = configuration;
        _connectTokenApi = connectTokenApi;
    }

    /// <summary>
    /// 以当前访问令牌换取 impersonation 令牌。
    /// extraParameters 携带目标（tenant_id / user_id / delegation_id），grant 据此识别目标并复查权限。
    /// clientId 为当前令牌所属客户端（从当前用户 client_id claim 读取），空时回落默认客户端。
    /// </summary>
    public virtual async Task<ImpersonationResultDto> ExchangeAsync(
        string accessToken,
        IReadOnlyDictionary<string, string> extraParameters,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        if (accessToken.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTokenExchangeFailed)
                .WithData("reason", "当前请求未携带访问令牌");
        }

        var authority = _configuration["AuthServer:Authority"];
        if (authority.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTokenExchangeFailed)
                .WithData("reason", "AuthServer:Authority 未配置");
        }

        // grant_type 统一取 Domain.Shared 常量（与 ExtensionGrant 及 Domain 种子同源），避免字面量漂移
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = AbpAdminOpenIddictDefaults.GrantTypes.Impersonation,
            ["client_id"] = clientId ?? "AbpAdmin_App"
        };
        foreach (var (key, value) in extraParameters)
        {
            form[key] = value;
        }

        using var response = await _connectTokenApi.ExchangeAsync(
            form, authorization: $"Bearer {accessToken}", cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 标准 OAuth 错误响应 {"error":"invalid_grant","error_description":"..."}
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTokenExchangeFailed)
                .WithData("reason", response.Result.ReadOAuthError() ?? $"HTTP {(int)response.StatusCode}");
        }

        var payload = response.Result;
        if (payload?.AccessToken.IsNullOrWhiteSpace() != false)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTokenExchangeFailed)
                .WithData("reason", "token 端点响应缺少 access_token");
        }

        // 直接返回 Contracts DTO（round4 审查 R1：原包装类与 ImpersonationResultDto 字段逐一相同，
        // 中间映射是纯拷贝，删掉——与 OpenIddictTokenExchanger 直接返回 GenerateAccessTokenResultDto 同款）
        return new ImpersonationResultDto
        {
            AccessToken = payload.AccessToken!,
            TokenType = payload.TokenType.IsNullOrWhiteSpace() ? "Bearer" : payload.TokenType!,
            ExpiresIn = payload.ExpiresIn ?? 0,
            RefreshToken = payload.RefreshToken
        };
    }
}
