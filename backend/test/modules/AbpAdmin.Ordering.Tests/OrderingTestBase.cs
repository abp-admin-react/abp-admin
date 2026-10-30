using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.FeatureManagement;

namespace AbpAdmin.Ordering;

/// <summary>
/// 订单模块测试模块：拉 Ordering + 测试基座，程序集专属 Testcontainers PG 库真迁移建表。
/// 不调 AddAlwaysDisableUnitOfWorkTransaction：订单状态机的验收点就是共享事务的
/// 提交/回滚语义（M1 第一条测试），测试里要真事务。
/// </summary>
[DependsOn(
    typeof(AbpAdminOrderingModule),
    typeof(AbpAdminTestBaseModule)
)]
public class OrderingTestModule : AbpModule
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

        var database = AbpAdminTestPg.GetDatabase(typeof(OrderingTestModule).Assembly);
        database.MigrateOnce("AbpAdminDbContext", () =>
        {
            using var dbContext = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(database.ConnectionString).Options);
            dbContext.Database.Migrate();
        });
        database.MigrateOnce("OrderingDbContext", () =>
        {
            using var dbContext = new Data.OrderingDbContext(
                new DbContextOptionsBuilder<Data.OrderingDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(OrderingConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });

        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = database.ConnectionString;
        });
    }
}

/// <summary>
/// 桩模块（ReplaceServices 换掉集成服务实现）：订单状态机用例全部走这里——
/// 不依赖收款实现，渠道行为由 StubCollectionIntegrationService 配置。
/// </summary>
[DependsOn(typeof(OrderingTestModule))]
public class OrderingStubTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 集成服务换成桩（ReplaceServices 语义）：订单状态机用例不依赖收款实现，
        // 渠道行为由 StubCollectionIntegrationService 配置（计划第 7 节 M1）
        context.Services.Replace(ServiceDescriptor.Singleton
            <AbpAdmin.Collection.Contracts.ICollectionIntegrationService, Stubs.StubCollectionIntegrationService>());
    }
}

/// <summary>
/// 共享事务测试模块（M1 第一条测试）：同时拉真实 Collection + Ordering 模块——
/// 两个 DbContext 都用 Default 连接串，在同一工作单元里写入两边的行。
/// </summary>
[DependsOn(
    typeof(OrderingTestModule),          // 框架/订单上下文的迁移与连接串接线复用（同一程序集的同一测试库）
    typeof(Collection.AbpAdminCollectionModule)
)]
public class SharedTransactionTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 只补 Collection 上下文的建表（其余接线由 OrderingTestModule 完成：
        // 整块复制会让两份接线各自漂移）
        var database = AbpAdminTestPg.GetDatabase(typeof(OrderingTestModule).Assembly);
        database.MigrateOnce("CollectionDbContext", () =>
        {
            using var dbContext = new Collection.Data.CollectionDbContext(
                new DbContextOptionsBuilder<Collection.Data.CollectionDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(Collection.CollectionConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });
    }
}

/// <summary>订单测试基类（真模块；桩用例继承 <see cref="OrderingStubTestBase"/>）。</summary>
public abstract class OrderingTestBase : AbpAdminTestBase<OrderingTestModule>
{
    protected OrderingTestBase()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
    }
}

/// <summary>集成服务换成桩的订单测试基类。</summary>
public abstract class OrderingStubTestBase : AbpAdminTestBase<OrderingStubTestModule>
{
    protected OrderingStubTestBase()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
    }

    protected Stubs.StubCollectionIntegrationService Stub =>
        (Stubs.StubCollectionIntegrationService)GetRequiredService<AbpAdmin.Collection.Contracts.ICollectionIntegrationService>();
}
