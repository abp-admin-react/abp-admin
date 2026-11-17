using AbpAdmin.Biz.Template;
using AbpAdmin.Collection;
using AbpAdmin.Ledger;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Ordering;
using AbpAdmin.Webhooks;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpDistributedLockingModule),
    typeof(AbpAdminEntityFrameworkCoreModule),
    typeof(AbpAdminApplicationContractsModule),
    // 业务模块挂接点：每新增一个业务模块加一行（其 IAbpAdminDbSchemaMigrator 随模块加载自动被迁移循环枚举）
    typeof(AbpAdminBizTemplateModule),
    typeof(AbpAdminWebhooksModule),
    typeof(AbpAdminCollectionModule),
    typeof(AbpAdminOrderingModule),
    typeof(AbpAdminLedgerModule),
    typeof(AbpAdmin.TokenGateway.AbpAdminTokenGatewayModule) // HarnessBot 路线 C 外层
)]
public class AbpAdminDbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Modules.DeploymentLockingConfiguration.Configure(context.Services, context.Services.GetConfiguration());
    }
}
