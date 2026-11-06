using System;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 管理资格：未登记、未声明、未认证、租户未就绪、权限失败一律降级 Public。
/// </summary>
public class AdminErrorEligibilityResolver : IAdminErrorEligibilityResolver, ITransientDependency
{
    public AdminErrorEligibilityResult Resolve(AdminErrorEligibilityContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        try
        {
            var httpContext = context.HttpContext;
            if (httpContext == null)
            {
                return Degrade(AdminEligibilityDegradeReason.EligibilityQueryFailed, "HttpContext is null");
            }

            if (context.EndpointRegistration == null)
            {
                return Degrade(AdminEligibilityDegradeReason.NotRegistered);
            }

            if (context.EndpointRegistration.Audience != ClientErrorAudience.AdminCompatibility)
            {
                return Degrade(AdminEligibilityDegradeReason.NotDeclaredAdminCompatibility);
            }

            var services = httpContext.RequestServices;
            var user = services?.GetService<ICurrentUser>();
            var isAuthenticated = user?.IsAuthenticated
                                  ?? httpContext.User.Identity?.IsAuthenticated == true;
            if (!isAuthenticated)
            {
                return Degrade(AdminEligibilityDegradeReason.Unauthenticated);
            }

            var currentTenant = services?.GetService<ICurrentTenant>();
            if (currentTenant is { IsAvailable: false } && user?.TenantId != null)
            {
                return Degrade(AdminEligibilityDegradeReason.SessionOrTenantNotReady);
            }

            if (context.EndpointRegistration.RequiredPermissions is { Count: > 0 })
            {
                var permissionChecker = services?.GetService<IPermissionChecker>();
                if (permissionChecker == null)
                {
                    return Degrade(AdminEligibilityDegradeReason.EligibilityQueryFailed, "IPermissionChecker missing");
                }

                foreach (var permission in context.EndpointRegistration.RequiredPermissions)
                {
                    if (!permissionChecker.IsGrantedAsync(permission).GetAwaiter().GetResult())
                    {
                        return Degrade(AdminEligibilityDegradeReason.PermissionDenied);
                    }
                }
            }

            return new AdminErrorEligibilityResult
            {
                IsEligible = true,
                DegradeReason = null,
            };
        }
        catch (Exception ex)
        {
            return Degrade(AdminEligibilityDegradeReason.EligibilityQueryFailed, ex.GetType().Name);
        }
    }

    private static AdminErrorEligibilityResult Degrade(
        AdminEligibilityDegradeReason reason,
        string? details = null)
    {
        return new AdminErrorEligibilityResult
        {
            IsEligible = false,
            DegradeReason = reason,
            Details = details,
        };
    }
}
