using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;
using Volo.Abp.Localization.External;

namespace AbpAdmin.HttpApi.ClientErrors.Localization;

/// <summary>
/// 替换独立本地化服务及其具体类型，覆盖脚本控制器的具体类注入。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(
    typeof(IAbpApplicationLocalizationAppService),
    typeof(AbpApplicationLocalizationAppService),
    typeof(ClientErrorApplicationLocalizationAppService))]
public class ClientErrorApplicationLocalizationAppService : AbpApplicationLocalizationAppService
{
    private readonly PublicLocalizationViewFilter _filter;

    public ClientErrorApplicationLocalizationAppService(
        IExternalLocalizationStore externalLocalizationStore,
        IOptions<AbpLocalizationOptions> localizationOptions,
        PublicLocalizationViewFilter filter)
        : base(externalLocalizationStore, localizationOptions)
    {
        _filter = filter;
    }

    public override async Task<ApplicationLocalizationDto> GetAsync(ApplicationLocalizationRequestDto input)
    {
        var dto = await base.GetAsync(input);
        return _filter.FilterLocalization(dto);
    }
}
