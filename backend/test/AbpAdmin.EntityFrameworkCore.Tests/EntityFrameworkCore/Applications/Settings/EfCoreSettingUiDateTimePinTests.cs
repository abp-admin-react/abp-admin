using AbpAdmin.Settings;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Settings;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreSettingUiDateTimePinTests : SettingUiDateTimePinTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
