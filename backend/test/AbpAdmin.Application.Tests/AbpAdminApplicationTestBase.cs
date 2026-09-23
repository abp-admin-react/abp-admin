using Volo.Abp.Modularity;

namespace AbpAdmin;

public abstract class AbpAdminApplicationTestBase<TStartupModule> : AbpAdminTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
