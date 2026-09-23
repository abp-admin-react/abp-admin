using Volo.Abp.Modularity;

namespace AbpAdmin;

[DependsOn(
    typeof(AbpAdminDomainModule),
    typeof(AbpAdminTestBaseModule)
)]
public class AbpAdminDomainTestModule : AbpModule
{

}
