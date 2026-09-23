using System;
using AbpAdmin.Account;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin;

/// <summary>
/// 从当前 HTTP 请求的 Authorization 头读取访问令牌原文。
/// AccountProAppService 做模拟登录令牌交换时把它转发给 /connect/token，
/// 使 impersonation 扩展授权能识别当前用户并复查权限。
/// </summary>
public class HttpContextCurrentAccessTokenProvider : ICurrentAccessTokenProvider, ITransientDependency
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentAccessTokenProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetAccessToken()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return header["Bearer ".Length..].Trim();
    }
}
