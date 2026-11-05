using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 管理资格解析器实现。
/// 实现 IAdminErrorEligibilityResolver 契约，按方案 4.2 判定管理兼容性资格。
/// </summary>
public class AdminErrorEligibilityResolver : IAdminErrorEligibilityResolver
{
    public AdminErrorEligibilityResult Resolve(AdminErrorEligibilityContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var httpContext = context.HttpContext;

        if (httpContext == null)
        {
            return new AdminErrorEligibilityResult
            {
                IsEligible = false,
                DegradeReason = AdminEligibilityDegradeReason.EligibilityQueryFailed,
                Details = "HttpContext is null",
            };
        }

        if (context.EndpointRegistration == null)
        {
            return new AdminErrorEligibilityResult
            {
                IsEligible = false,
                DegradeReason = AdminEligibilityDegradeReason.NotRegistered,
            };
        }

        if (context.EndpointRegistration.Audience != ClientErrorAudience.AdminCompatibility)
        {
            return new AdminErrorEligibilityResult
            {
                IsEligible = false,
                DegradeReason = AdminEligibilityDegradeReason.NotDeclaredAdminCompatibility,
            };
        }

        var user = httpContext.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
        {
            return new AdminErrorEligibilityResult
            {
                IsEligible = false,
                DegradeReason = AdminEligibilityDegradeReason.Unauthenticated,
            };
        }

        if (context.EndpointRegistration.RequiredPermissions != null
            && context.EndpointRegistration.RequiredPermissions.Count > 0)
        {
            var authorizationService = httpContext.RequestServices
                .GetService<IAuthorizationService>();

            if (authorizationService != null)
            {
                foreach (var permission in context.EndpointRegistration.RequiredPermissions)
                {
                    var authorized = authorizationService
                        .AuthorizeAsync(user, permission)
                        .Result;

                    if (!authorized.Succeeded)
                    {
                        return new AdminErrorEligibilityResult
                        {
                            IsEligible = false,
                            DegradeReason = AdminEligibilityDegradeReason.PermissionDenied,
                        };
                    }
                }
            }
        }

        return new AdminErrorEligibilityResult
        {
            IsEligible = true,
            DegradeReason = null,
        };
    }
}