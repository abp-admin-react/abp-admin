using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

// 仅保留 collection 定义（~80 个测试类的 [Collection(AbpAdminTestConsts.CollectionDefinitionName)]
// 仍指向它）；原挂载的空壳 fixture 已删——共享 PG 库的串行化由程序集级
// DisableTestParallelization（TestPgAssembly.cs）承担，collection 无剩余职责。
[CollectionDefinition(AbpAdminTestConsts.CollectionDefinitionName)]
public class AbpAdminEntityFrameworkCoreCollection
{

}
