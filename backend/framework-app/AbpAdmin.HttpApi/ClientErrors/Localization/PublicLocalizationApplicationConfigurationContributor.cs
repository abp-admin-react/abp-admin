using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;

namespace AbpAdmin.HttpApi.ClientErrors.Localization;

/// <summary>
/// 裁剪 application-configuration 的 localization 段为公开视图。与匿名 settings 裁剪共存。
/// </summary>
public sealed class PublicLocalizationApplicationConfigurationContributor : IApplicationConfigurationContributor
{
    public Task ContributeAsync(ApplicationConfigurationContributorContext context)
    {
        var filter = context.ServiceProvider.GetRequiredService<PublicLocalizationViewFilter>();
        filter.FilterConfiguration(context.ApplicationConfiguration);
        return Task.CompletedTask;
    }
}
