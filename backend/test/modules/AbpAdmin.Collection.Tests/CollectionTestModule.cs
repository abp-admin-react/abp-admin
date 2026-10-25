using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.FeatureManagement;

namespace AbpAdmin.Collection;

/// <summary>
/// 收款模块测试模块：程序集专属 Testcontainers PG 库真迁移建表（框架 + Collection + Ledger 三本账）。
/// 不调 AddAlwaysDisableUnitOfWorkTransaction：CreateAsync/CancelAsync 的验收点就是
/// requiresNew 提交可见性与状态机事务语义。
/// 提现 M2 起 DependsOn Ledger（提现/充值过账走 ILedgerIntegrationService，测试基类的
/// IDataSeeder 会把 CollectionLedgerSeedProvider 的科目与模板种进 Fin 表）。
/// </summary>
[DependsOn(
    typeof(AbpAdminCollectionModule),
    typeof(AbpAdmin.Ledger.AbpAdminLedgerModule),
    typeof(AbpAdminTestBaseModule)
)]
public class CollectionTestModule : AbpModule
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

        var database = AbpAdminTestPg.GetDatabase(typeof(CollectionTestModule).Assembly);
        database.MigrateOnce("AbpAdminDbContext", () =>
        {
            using var dbContext = new AbpAdminDbContext(
                new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(database.ConnectionString).Options);
            dbContext.Database.Migrate();
        });
        database.MigrateOnce("CollectionDbContext", () =>
        {
            using var dbContext = new Data.CollectionDbContext(
                new DbContextOptionsBuilder<Data.CollectionDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(CollectionConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });
        database.MigrateOnce("LedgerDbContext", () =>
        {
            using var dbContext = new AbpAdmin.Ledger.Data.LedgerDbContext(
                new DbContextOptionsBuilder<AbpAdmin.Ledger.Data.LedgerDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(AbpAdmin.Ledger.LedgerConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });

        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = database.ConnectionString;
        });

        // 链网管理服务从 IConfiguration 读 Collection:MasterKey（解密助记词/RPC/KEY 密文）——
        // 测试用与 HdKeyService.Protect 相同的 "test-master-key"，叠加在既有配置之上
        var testConfig = new ConfigurationBuilder()
            .AddConfiguration(context.Services.GetConfiguration())
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Collection:MasterKey"] = "test-master-key",
            })
            .Build();
        context.Services.Replace(
            ServiceDescriptor.Singleton<
                Microsoft.Extensions.Configuration.IConfiguration>(testConfig));

        // 渠道网关假实现：静态双实例（微信/支付宝）单例注册——测试先配置行为再调被测服务
        context.Services.AddSingleton<Channels.ICollectionChannelGateway>(Fakes.FakeChannelGateway.Wechat);
        context.Services.AddSingleton<Channels.ICollectionChannelGateway>(Fakes.FakeChannelGateway.Alipay);
    }
}
