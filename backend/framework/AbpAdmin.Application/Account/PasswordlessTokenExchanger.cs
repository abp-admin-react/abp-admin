using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.OpenIddict;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Account;

/// <summary>
/// passwordless 扩展授权的 /connect/token 交换器（Magic Link / OTP 登录的服务端换票入口）。
/// 与 ImpersonationTokenExchanger 同构：HTTP 细节走 IConnectTokenApi，OAuth 错误统一转业务异常。
/// 三点差异：凭据校验与一次性消费在 grant 内的 PasswordlessLoginManager 完成（限流语义不变）；
/// 内层请求必须携带 __tenant 头——凭据缓存键与用户查找都在该租户上下文中解析，
/// 匿名调用没有当前用户可依赖；
/// 失败一律映射为 InvalidMagicLink 且原因只进服务端日志——本端点面向匿名调用方，
/// WithData 会随 ABP error.data 序列化给客户端，透传 OAuth error / 基础设施状态
/// 可被用于探测限流与部署情况（对比：impersonation/linked 换票面向已认证调用方，保留 WithData）。
/// </summary>
public class PasswordlessTokenExchanger : ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IConnectTokenApi _connectTokenApi;
    private readonly ILogger<PasswordlessTokenExchanger> _logger;

    public PasswordlessTokenExchanger(
        IConfiguration configuration,
        IConnectTokenApi connectTokenApi,
        ILogger<PasswordlessTokenExchanger> logger)
    {
        _configuration = configuration;
        _connectTokenApi = connectTokenApi;
        _logger = logger;
    }

    /// <summary>
    /// 以一次性凭据换取登录令牌。code 与 magicLinkToken 二选一（grant 侧强校验）。
    /// tenantId 决定内层 /connect/token 请求的租户上下文，host 登录传 null。
    /// </summary>
    public virtual async Task<ImpersonationResultDto> ExchangeAsync(
        string email,
        string? code,
        string? magicLinkToken,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_configuration["AuthServer:Authority"].IsNullOrWhiteSpace())
        {
            _logger.LogWarning("passwordless 换票失败：AuthServer:Authority 未配置");
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
        }

        // clientId 与 Domain 种子同源（OpenIddict:Applications:AbpAdmin_App:ClientId），
        // 种子读不到时同样回落字面量——匿名换票没有当前令牌可取 client_id claim
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = AbpAdminOpenIddictDefaults.GrantTypes.Passwordless,
            ["client_id"] = _configuration["OpenIddict:Applications:AbpAdmin_App:ClientId"] ?? "AbpAdmin_App",
            ["email"] = email
        };
        if (!code.IsNullOrWhiteSpace())
        {
            form["code"] = code;
        }
        if (!magicLinkToken.IsNullOrWhiteSpace())
        {
            form["magic_link_token"] = magicLinkToken;
        }

        using var response = await _connectTokenApi.ExchangeAsync(
            form,
            authorization: null,
            tenantId: tenantId?.ToString(),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "passwordless 换票失败：email={Email}, tenantId={TenantId}, reason={Reason}",
                email, tenantId, response.Result.ReadOAuthError() ?? $"HTTP {(int)response.StatusCode}");
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
        }

        var payload = response.Result;
        if (payload?.AccessToken.IsNullOrWhiteSpace() != false)
        {
            _logger.LogWarning(
                "passwordless 换票失败：token 端点响应缺少 access_token, email={Email}", email);
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
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
