using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Tenants;

/// <summary>
/// 授权请求带单一 hostLogin=1、且没有具体租户时，把当前租户定为主机。
/// 只在 /connect/authorize 上清掉旧租户 Cookie，账户页和换票才继续落在主机。
/// </summary>
internal sealed class HostLoginTenantResolveContributor : TenantResolveContributorBase
{
    private const string AuthorizePath = "/connect/authorize";
    private const string HostLoginParameter = "hostLogin";

    public override string Name => "HostLogin";

    public override Task ResolveAsync(ITenantResolveContext context)
    {
        var services = ((IServiceProviderAccessor)context).ServiceProvider;
        var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext is null || !IsHostLogin(httpContext.Request))
        {
            return Task.CompletedTask;
        }

        context.Handled = true;
        context.TenantIdOrName = null;

        // 只在授权请求上清 Cookie。账户页沿用这次结果，换票才不会回到旧租户。
        var tenantKey = services.GetRequiredService<IOptions<AbpAspNetCoreMultiTenancyOptions>>().Value.TenantKey;
        if (httpContext.Request.Path.Equals(AuthorizePath, StringComparison.OrdinalIgnoreCase)
            && httpContext.Request.Cookies.ContainsKey(tenantKey))
        {
            AbpMultiTenancyCookieHelper.SetTenantCookie(httpContext, null, tenantKey);
        }

        return Task.CompletedTask;
    }

    internal static bool IsHostLogin(HttpRequest request)
    {
        if (request.Path.Equals(AuthorizePath, StringComparison.OrdinalIgnoreCase))
        {
            // 空白 __tenant 不当成租户，否则 hostLogin=1 会退回旧的租户 Cookie。
            return !HasConcreteTenant(request.Query) && IsSingleHostMarker(request.Query);
        }

        if (!IsAccountLoginPath(request.Path)
            || request.Query["ReturnUrl"] is not { Count: 1 } returnUrls
            || returnUrls[0] is not { } returnUrl)
        {
            return false;
        }

        var separator = returnUrl.IndexOf('?');
        if (separator < 0 || !returnUrl.AsSpan(0, separator).Equals(
                string.Concat(request.PathBase.Value, AuthorizePath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 只看回跳的 authorize 查询。登录页自己的 __tenant 不能否决其中的 hostLogin。
        var parameters = QueryHelpers.ParseQuery(returnUrl[separator..]);
        return !HasConcreteTenant(parameters) && IsSingleHostMarker(parameters);
    }

    private static bool HasConcreteTenant(IQueryCollection query)
    {
        return query.TryGetValue("__tenant", out var values) && HasText(values);
    }

    private static bool HasConcreteTenant(System.Collections.Generic.Dictionary<string, StringValues> query)
    {
        return query.TryGetValue("__tenant", out var values) && HasText(values);
    }

    private static bool HasText(StringValues values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSingleHostMarker(IQueryCollection query)
    {
        return query.TryGetValue(HostLoginParameter, out var values) && IsSingleHostMarker(values);
    }

    private static bool IsSingleHostMarker(System.Collections.Generic.Dictionary<string, StringValues> query)
    {
        return query.TryGetValue(HostLoginParameter, out var values) && IsSingleHostMarker(values);
    }

    private static bool IsSingleHostMarker(StringValues values)
    {
        return values.Count == 1 && string.Equals(values[0]?.Trim(), "1", StringComparison.Ordinal);
    }

    private static bool IsAccountLoginPath(PathString path)
    {
        return path.Equals("/Account/Login", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/Account/TwoFactorVerification", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/Account/Register", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/Account/ResetPassword", StringComparison.OrdinalIgnoreCase);
    }
}
