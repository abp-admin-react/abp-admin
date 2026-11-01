using System;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Localization;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Collection;

/// <summary>
/// 自包含收款模块（照 Biz.Template / Webhooks 模板接线）：宿主/DbMigrator 各加一行 DependsOn，
/// 迁移随本工程（__AbpAdminCollection_EFMigrationsHistory）。
/// 依赖方向（计划第 2 节）：Collection → Collection.Contracts；不引用 Ordering，
/// 不查询订单表。微信走 EasyAbp（经框架 EF 工程与直接引用）。
/// 扫链/归集的 BackgroundService 不在本模块注册——由 AbpAdmin.HttpApi.Host 调
/// AddCollectionChainWorkers()（M5），DbMigrator 不得拉起它们。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreModule), // EF 基建 + IAbpAdminDbSchemaMigrator 约定接口
    typeof(AbpAdminCollectionContractsModule), // 契约（集成服务接口/ETO/错误码）
    typeof(AbpAspNetCoreMvcModule),            // Auto API：ConventionalControllers
    typeof(AbpAuthorizationModule),
    typeof(AbpLocalizationModule),
    typeof(AbpVirtualFileSystemModule)
    // 设置存储复用框架 SettingManagement（同 Biz.Template：经 AbpAdminDomainModule 链加载，
    // SettingUi/SettingManagement 的包引用见 csproj，不在这里重复 DependsOn）
)]
public class AbpAdminCollectionModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<CollectionDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        // 链上告警出口与网关工厂（M5）：模块默认注册（收件人空名单=只落日志、无 Etherscan 兜底），
        // 宿主 AddCollectionChainWorkers 用真实配置覆盖（收件人/端点/主密钥）。
        context.Services.AddSingleton<Chain.ChainAlertService>(sp => new Chain.ChainAlertService(
            Array.Empty<Guid>(),
            sp.GetRequiredService<Volo.Abp.EventBus.Distributed.IDistributedEventBus>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Chain.ChainAlertService>>()));
        context.Services.AddSingleton<Chain.ChainGatewayFactory>(sp => new Chain.ChainGatewayFactory(
            new System.Net.Http.HttpClient(),
            sp.GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>(),
            "https://api.etherscan.io/v2/api",
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));

        // 上下文级 UseNpgsql + 独立 History 表；schema 从连接串 SearchPath 显式解析
        //（MigrationHistorySchemaResolver，与 Webhooks/Ordering 共用，理由见该类注释）。
        var configuration = context.Services.GetConfiguration();
        var (historySchema, _) = AbpAdmin.EntityFrameworkCore.MigrationHistorySchemaResolver.Resolve(
            configuration.GetConnectionString(CollectionConsts.ConnectionStringName)
            ?? string.Empty);
        CollectionDbSchemaMigrator.HistorySchema = historySchema;
        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<CollectionDbContext>(contextOptions =>
            {
                contextOptions.UseNpgsql(npgsql =>
                {
                    npgsql.MigrationsHistoryTable(CollectionConsts.SchemaHistoryTable, historySchema);
                });
            });
        });

        // 显式注册迁移器：框架迁移循环按 IAbpAdminDbSchemaMigrator 接口枚举（模块接线契约）
        context.Services.AddTransient<IAbpAdminDbSchemaMigrator, CollectionDbSchemaMigrator>();

        // 集成服务实现：本地 DI 显式注册（同进程调用；远程化是以后的契约变更）
        context.Services.AddTransient<Contracts.ICollectionIntegrationService, CollectionIntegrationService>();

        // Auto API（/api/app/collection-admin 一类，随管理端应用服务落地）
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AbpAdminCollectionModule).Assembly);
        });

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminCollectionModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<CollectionResource>("en")
                .AddVirtualJson("/Localization/Collection");

            // 设置页分组名挂到 SettingUiResource 链（同 Biz.Template 的接线与取舍说明）
            options.Resources
                .Get<EasyAbp.Abp.SettingUi.Localization.SettingUiResource>()
                .AddVirtualJson("/Localization/Collection");
        });

        // 错误码 → 模块资源映射（BusinessException 的 code 带 "AbpAdmin.Collection:" 前缀）
        Configure<Volo.Abp.Localization.ExceptionHandling.AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("AbpAdmin.Collection", typeof(CollectionResource));
        });
    }
}
