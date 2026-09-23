using AbpAdmin.DataDictionaries;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreDataDictionarySeedTests : DataDictionarySeedTests<AbpAdminEntityFrameworkCoreTestModule>
{
}
