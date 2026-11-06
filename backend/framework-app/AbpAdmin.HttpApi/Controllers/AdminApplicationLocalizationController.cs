using System.Threading.Tasks;
using AbpAdmin.HttpApi.ClientErrors.Localization;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;

namespace AbpAdmin.Controllers;

/// <summary>
/// 管理专用本地化入口。不放宽 Shared 的 /api/abp/*。
/// </summary>
[RemoteService(Name = "AbpAdmin")]
[Route("api/abp-admin/application-localization")]
[Authorize(AbpAdminPermissions.LanguageTexts.Default)]
public class AdminApplicationLocalizationController : AbpController
{
    private readonly AbpApplicationLocalizationAppService _localizationAppService;

    public AdminApplicationLocalizationController(AbpApplicationLocalizationAppService localizationAppService)
    {
        _localizationAppService = localizationAppService;
    }

    [HttpGet]
    public async Task<ApplicationLocalizationDto> GetAsync([FromQuery] ApplicationLocalizationRequestDto input)
    {
        using (PublicLocalizationViewFilter.EnableAdminScope())
        {
            return await _localizationAppService.GetAsync(input);
        }
    }
}
