using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreWebhookSubscriptionAppServiceTests : WebhookSubscriptionAppServiceTests<WebhooksTestsModule>
{
}
