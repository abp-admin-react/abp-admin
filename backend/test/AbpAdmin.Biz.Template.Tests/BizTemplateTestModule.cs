using AbpAdmin.Biz.Template.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 模板模块的独立测试模块：不挂框架 Application 巨链（那些由 Application.Tests/EFCore.Tests 覆盖），
/// 只拉 Biz 模块 + 测试基座，用内存 SQLite 建两张上下文的表。
/// 被测模块经 AbpAdminEntityFrameworkCoreModule 连带 AbpAdminDomainModule（Identity/权限/设置等域），
/// TestBase 启动时的 IDataSeeder 会跑全部域的种子贡献者——所以框架表（AbpAdminDbContext）必须一并建，
/// 否则框架种子先炸；BizTemplateDbContext 的表另建，模块自身用例跑在上面。
/// </summary>
[DependsOn(
    typeof(AbpAdminBizTemplateModule),
    typeof(AbpAdminTestBaseModule)
)]
public class BizTemplateTestModule : AbpModule
{
    private AbpUnitTestSqliteDatabase? _database;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        // 与框架 EFCore.Tests 同款：内存 SQLite 不需要 Busy 等待
        PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 与 EFCore.Tests 同口径：静态权限/特性不落库，减少对框架种子表的依赖面
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });

        // 内存 SQLite（每个测试实例独立建库，互不污染）
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();
        ConfigureInMemorySqlite(context.Services);
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _database = new AbpUnitTestSqliteDatabase();
        _database.CreateTables(
            new AbpAdminDbContext(new DbContextOptionsBuilder<AbpAdminDbContext>()
                .UseSqlite(_database.ConnectionString).Options));
        _database.CreateTables(
            new BizTemplateDbContext(new DbContextOptionsBuilder<BizTemplateDbContext>()
                .UseSqlite(_database.ConnectionString).Options));

        // 模块连接串名是 "Default"（BizTemplateConsts.ConnectionStringName），与框架同库
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

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _database?.Dispose();
    }
}
