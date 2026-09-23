using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.Identity;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Uow;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Volo.Abp.Studio;
using EasyAbp.Abp.DataDictionary.EntityFrameworkCore;
using EasyAbp.FileManagement.EntityFrameworkCore;
using EasyAbp.NotificationService.EntityFrameworkCore;
using EasyAbp.PaymentService.EntityFrameworkCore;
using EasyAbp.PaymentService.Prepayment.EntityFrameworkCore;
using EasyAbp.PaymentService.WeChatPay.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore;

[DependsOn(
    typeof(AbpAdminDomainModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule),
    typeof(AbpBackgroundJobsEntityFrameworkCoreModule),
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(AbpFeatureManagementEntityFrameworkCoreModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpTenantManagementEntityFrameworkCoreModule),
    typeof(BlobStoringDatabaseEntityFrameworkCoreModule),
    typeof(FileManagementEntityFrameworkCoreModule),
    typeof(AbpDataDictionaryEntityFrameworkCoreModule),
    typeof(NotificationServiceEntityFrameworkCoreModule),
    typeof(PaymentServiceEntityFrameworkCoreModule),
    typeof(PaymentServicePrepaymentEntityFrameworkCoreModule),
    typeof(PaymentServiceWeChatPayEntityFrameworkCoreModule)
    )]
public class AbpAdminEntityFrameworkCoreModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        AbpAdminEfCoreEntityExtensionMappings.Configure();

        if (AbpAdminDatabaseProvider.IsPostgreSql(context.Services.GetConfiguration()))
        {
            // Npgsql 6+ 默认把 timestamp 按 timestamptz（UTC）处理；ABP 实体沿用本地 DateTime 语义，
            // 打开官方文档推荐的兼容开关，避免存量 timestamp 列读写偏移。与各设计期/测试工厂保持一致。
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var usePostgreSql = AbpAdminDatabaseProvider.IsPostgreSql(configuration);

        context.Services.AddAbpDbContext<AbpAdminDbContext>(options =>
        {
                /* Remove "includeAllEntities: true" to create
                 * default repositories only for aggregate roots */
            options.AddDefaultRepositories(includeAllEntities: true);
            // 复合键连接表（UserId+OrganizationUnitId）不在默认仓储注册范围内，
            // UserExcelBuilder 的 OU 批量查询需要无键 IRepository<TEntity>，显式补注册
            options.AddRepository<IdentityUserOrganizationUnit,
                EfCoreRepository<AbpAdminDbContext, IdentityUserOrganizationUnit>>();
        });

        if (AbpStudioAnalyzeHelper.IsInAnalyzeMode)
        {
            return;
        }

        Configure<AbpDbContextOptions>(options =>
        {
            /* 数据库提供程序主切换点：读 Database:Provider 配置（单一来源见 HttpApi.Host/appsettings.json）。
             * 建表不走 EF 迁移程序集，由 EntityFrameworkCoreAbpAdminDbSchemaMigrator 执行本工程 Sql/ 下的脚本。 */
            if (usePostgreSql)
            {
                options.UseNpgsql();
            }
            else
            {
                options.UseSqlite();
            }
        });

        if (!usePostgreSql)
        {
            // SQLite：ABP UoW 事务全局禁用（框架对 SQLite 嵌套事务限制的标准做法）。
            // 代价（问题10）：开发链路（DbMigrator 种子、后台作业）无 UoW 事务原子性，
            // 种子中途失败会留"半套数据"——恢复方式：重跑 DbMigrator 续种（贡献者幂等），
            // 或删库重跑。生产 PostgreSQL 不受影响（本分支不执行）。
            context.Services.AddAlwaysDisableUnitOfWorkTransaction();
            Configure<AbpUnitOfWorkDefaultOptions>(options =>
            {
                options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled;
            });
        }
    }
}
