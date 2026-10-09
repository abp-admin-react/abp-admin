using AbpAdmin.Biz.Template.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 模板模块的独立测试模块：不挂框架 Application 巨链（那些由 Application.Tests/EFCore.Tests 覆盖），
/// 只拉 Biz 模块 + 测试基座。数据库是本程序集专属的 Testcontainers PG 库（进程内共享，
/// 用例间由测试基类 Respawn 清表隔离），两张上下文都在其中真迁移建表。
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

        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        // 程序集专属 PG 库：先建框架上下文的表（种子依赖），再按模块同款独立 History 表
        //（__BizTemplate_EFMigrationsHistory）建 Biz 上下文的表——真迁移建表，
        // 每次全量测试顺带验证两条迁移链都可空库自举
        var database = AbpAdminTestPg.GetDatabase(typeof(BizTemplateTestModule).Assembly);
        database.MigrateOnce("AbpAdminDbContext", () =>
        {
            using var dbContext = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(database.ConnectionString).Options);
            dbContext.Database.Migrate();
        });
        database.MigrateOnce("BizTemplateDbContext", () =>
        {
            using var dbContext = new BizTemplateDbContext(
                new DbContextOptionsBuilder<BizTemplateDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(BizTemplateConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });

        // 模块连接串名是 "Default"（BizTemplateConsts.ConnectionStringName），与框架同库
        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = database.ConnectionString;
        });

        // 提供程序无需再覆盖：模块侧注册的上下文级 UseNpgsql（独立 History 表）即测试提供程序
    }
}
