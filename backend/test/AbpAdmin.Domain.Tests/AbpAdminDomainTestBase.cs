using Volo.Abp.Modularity;

namespace AbpAdmin;

/* Inherit from this class for your domain layer tests. */
public abstract class AbpAdminDomainTestBase<TStartupModule> : AbpAdminTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
