using System;
using AbpAdmin.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Uow;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.EntityFrameworkCore.Sqlite;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>
/// Webhook 模块测试的聚合模块：EF 测试基线 + Webhook 模块 + 内存 SQLite 双上下文覆盖。
/// 与 BizTemplateTestModule 同款：模块侧为 WebhooksDbContext 注册了上下文级 UseNpgsql
/// （独立 History 表），此处必须再以同上下文级 UseSqlite 覆盖（后注册者胜），
/// 否则种子/用例会用 SQLite 连接串建 Npgsql 连接。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreTestModule),
    typeof(AbpAdminWebhooksModule))]
public class WebhooksTestsModule : AbpModule
{
    private AbpUnitTestSqliteDatabase? _database;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 与基线同口径：静态权限/特性不落库
        Configure<Volo.Abp.FeatureManagement.FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<Volo.Abp.PermissionManagement.PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });

        context.Services.AddAlwaysDisableUnitOfWorkTransaction();
        ConfigureInMemorySqlite(context.Services);

        // 上下文级覆盖（必须在业务模块的 UseNpgsql 之后注册——本模块是依赖末端）
        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<WebhooksDbContext>(contextOptions =>
            {
                contextOptions.UseSqlite();
            });
        });
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _database = new AbpUnitTestSqliteDatabase();
        _database.CreateTables(
            new AbpAdminDbContext(new DbContextOptionsBuilder<AbpAdminDbContext>()
                .UseSqlite(_database.ConnectionString).Options));
        _database.CreateTables(
            new WebhooksDbContext(new DbContextOptionsBuilder<WebhooksDbContext>()
                .UseSqlite(_database.ConnectionString).Options));

        services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = _database.ConnectionString;
        });

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.UseSqlite();
            });
        });
    }
}
