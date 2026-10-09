using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.EntityFrameworkCore;

[DependsOn(
    typeof(AbpAdminApplicationTestModule),
    typeof(AbpAdminEntityFrameworkCoreModule)
)]
public class AbpAdminEntityFrameworkCoreTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
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

        // Testcontainers PG：本程序集专属库（进程内共享，用例间由测试基类 Respawn 清表隔离）。
        // 对 AbpAdminDbContext 跑真迁移建表——每次全量测试顺带验证框架迁移链可空库自举
        //（CI 另有 has-pending-model-changes 守模型漂移）。
        var database = AbpAdminTestPg.GetDatabase(typeof(AbpAdminEntityFrameworkCoreTestModule).Assembly);
        database.MigrateOnce("AbpAdminDbContext", () =>
        {
            using var dbContext = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(database.ConnectionString).Options);
            dbContext.Database.Migrate();
        });

        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = database.ConnectionString;
        });

        // 提供程序无需再覆盖：框架模块注册的 UseNpgsql 即测试提供程序——
        // 运行期 PG-only 语义在测试里同构成立（共享库多上下文各记各的迁移账）。
    }
}
