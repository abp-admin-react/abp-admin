using System.Threading.Tasks;
using AbpAdmin.Modules;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Data;
using Volo.Abp.Users;

namespace AbpAdmin.Controllers;

public sealed class DeploymentModulesApplicationConfigurationContributor : IApplicationConfigurationContributor
{
    public async Task ContributeAsync(ApplicationConfigurationContributorContext context)
    {
        if (!context.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated) return;
        var snapshot = await context.ServiceProvider.GetRequiredService<IDeploymentModuleStateReader>().GetAsync();
        context.ApplicationConfiguration.SetProperty("modules", new
        {
            version = snapshot.Version, initialized = snapshot.Initialized, states = snapshot.States
        });
    }
}
