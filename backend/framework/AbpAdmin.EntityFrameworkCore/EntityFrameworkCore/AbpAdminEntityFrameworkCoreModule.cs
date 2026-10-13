using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using System;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
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

        // Npgsql 6+ 默认把 timestamp 按 timestamptz（UTC）处理；ABP 实体沿用本地 DateTime 语义，
        // 打开官方文档推荐的兼容开关，避免存量 timestamp 列读写偏移。与设计时工厂、测试基建保持一致。
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
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
            // 唯一提供程序：PostgreSQL。建表走本工程的 EF Core 迁移（Migrations/），
            // 由 EntityFrameworkCoreAbpAdminDbSchemaMigrator 执行 Database.MigrateAsync()。
            options.UseNpgsql();
        });
    }
}
