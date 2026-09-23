using AbpAdmin.TextTemplates;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreStoredTemplateContentContributorTests : StoredTemplateContentContributorTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
