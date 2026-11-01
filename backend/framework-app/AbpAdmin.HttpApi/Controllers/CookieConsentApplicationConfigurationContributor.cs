using System.Threading.Tasks;
using AbpAdmin.Gdpr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Data;

namespace AbpAdmin.Controllers;

/// <summary>
/// 通过 application-configuration 端点下发 Cookie Consent 配置，
/// 前端在 getInitialState() 里即可拿到，不增加一次请求。
/// </summary>
public class CookieConsentApplicationConfigurationContributor : IApplicationConfigurationContributor
{
    public Task ContributeAsync(ApplicationConfigurationContributorContext context)
    {
        var options = context.ServiceProvider
            .GetRequiredService<IOptions<AbpAdminCookieConsentOptions>>().Value;

        context.ApplicationConfiguration.SetProperty(
            "cookieConsent",
            new
            {
                isEnabled = options.IsEnabled,
                cookiePolicyUrl = options.CookiePolicyUrl,
                privacyPolicyUrl = options.PrivacyPolicyUrl,
                expirationDays = options.Expiration.TotalDays
            });

        return Task.CompletedTask;
    }
}
