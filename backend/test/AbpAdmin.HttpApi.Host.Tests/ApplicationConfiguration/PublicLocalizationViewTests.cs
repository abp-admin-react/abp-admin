using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.HttpApi.ClientErrors.Localization;
using AbpAdmin.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Localization;
using Xunit;

namespace AbpAdmin.ApplicationConfiguration;

public class PublicLocalizationViewTests
{
    [RequiresHostDatabaseFact]
    public async Task Public_Localization_Should_Keep_Client_Resource_And_Drop_Admin()
    {
        var app = await HostTestBed.HostTask;
        var service = app.Services.GetRequiredService<IAbpApplicationLocalizationAppService>();
        var dto = await service.GetAsync(new ApplicationLocalizationRequestDto
        {
            CultureName = "en",
            OnlyDynamics = false,
        });

        dto.Resources.ShouldContainKey("AbpAdminClientErrors");
        dto.Resources.ShouldNotContainKey("AbpAdmin");
        dto.Resources.Values.SelectMany(r => r.Texts.Values)
            .ShouldNotContain(v => v.Contains("INTERNAL_ONLY_MARKER"));
    }

    [RequiresHostDatabaseFact]
    public async Task Configuration_With_Resources_Should_Use_Public_View()
    {
        var app = await HostTestBed.HostTask;
        var service = app.Services.GetRequiredService<IAbpApplicationConfigurationAppService>();
        var dto = await service.GetAsync(new ApplicationConfigurationRequestOptions
        {
            IncludeLocalizationResources = true,
        });

        dto.Localization.ShouldNotBeNull();
        dto.Localization.Languages.ShouldNotBeEmpty();
        var publicKeys = (dto.Localization.Resources is { Count: > 0 }
            ? dto.Localization.Resources.Keys.Select(key => key)
            : dto.Localization.Values.Keys.Select(key => key)).ToList();
        publicKeys.ShouldNotBeEmpty("IncludeLocalizationResources=true 必须下发公开资源文本。");
        publicKeys.ShouldNotContain("AbpAdmin");
        publicKeys.ShouldContain("AbpAdminClientErrors");
    }

    [RequiresHostDatabaseFact]
    public async Task Admin_Scope_Should_Restore_Internal_Resources()
    {
        var app = await HostTestBed.HostTask;
        var service = app.Services.GetRequiredService<AbpApplicationLocalizationAppService>();
        using (PublicLocalizationViewFilter.EnableAdminScope())
        {
            var dto = await service.GetAsync(new ApplicationLocalizationRequestDto
            {
                CultureName = "en",
                OnlyDynamics = false,
            });
            dto.Resources.ShouldContainKey("AbpAdmin");
        }
    }

    [RequiresHostDatabaseFact]
    public async Task Client_Static_Resource_Should_Not_Have_Database_Contributor()
    {
        var app = await HostTestBed.HostTask;
        var options = app.Services.GetRequiredService<IOptions<AbpLocalizationOptions>>().Value;
        options.Resources.ShouldContainKey("AbpAdminClientErrors");
        var resource = options.Resources["AbpAdminClientErrors"];
        resource.Contributors.ShouldNotContain(c => c is DbLocalizationResourceContributor);
        options.GlobalContributors.ShouldNotContain(t =>
            typeof(DbLocalizationResourceContributor).IsAssignableFrom(t));

        var factory = app.Services.GetRequiredService<IStringLocalizerFactory>();
        var localizer = factory.Create(typeof(ClientErrorResource));
        localizer["Unexpected"].ResourceNotFound.ShouldBeFalse();
    }
}
