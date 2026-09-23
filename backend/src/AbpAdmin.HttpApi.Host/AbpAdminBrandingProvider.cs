using Microsoft.Extensions.Localization;
using AbpAdmin.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace AbpAdmin;

[Dependency(ReplaceServices = true)]
public class AbpAdminBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<AbpAdminResource> _localizer;

    public AbpAdminBrandingProvider(IStringLocalizer<AbpAdminResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
