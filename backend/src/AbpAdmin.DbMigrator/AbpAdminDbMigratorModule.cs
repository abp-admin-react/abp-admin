using AbpAdmin.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace AbpAdmin.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAdminEntityFrameworkCoreModule),
    typeof(AbpAdminApplicationContractsModule)
)]
public class AbpAdminDbMigratorModule : AbpModule
{
}
