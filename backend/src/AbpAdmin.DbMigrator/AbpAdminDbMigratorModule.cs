using AbpAdmin.Biz.Template;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Webhooks;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace AbpAdmin.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpAdminEntityFrameworkCoreModule),
    typeof(AbpAdminApplicationContractsModule),
    // 业务模块挂接点：每新增一个业务模块加一行（其 IAbpAdminDbSchemaMigrator 随模块加载自动被迁移循环枚举）
    typeof(AbpAdminBizTemplateModule),
    typeof(AbpAdminWebhooksModule)
)]
public class AbpAdminDbMigratorModule : AbpModule
{
}
