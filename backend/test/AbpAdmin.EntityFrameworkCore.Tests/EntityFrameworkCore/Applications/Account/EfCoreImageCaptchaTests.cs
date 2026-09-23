using AbpAdmin.Captcha;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreImageCaptchaTests : ImageCaptchaTests<AbpAdminEntityFrameworkCoreTestModule>
{

}
