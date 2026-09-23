using AbpAdmin.Captcha;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Captcha;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreTurnstileValidatorTests : TurnstileValidatorTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
