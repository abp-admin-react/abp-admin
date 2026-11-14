using System;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Localization;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using HttpAgent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Collection;

/// <summary>
/// 收款模块使用独立的 EF 迁移历史，通过契约与订单模块通信，通过 ICollectionChannelGateway 接入渠道。
/// 链上任务和支付对账进程由 HttpApi.Host 注册，数据库迁移器仅应用迁移。
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
        // 宿主用部署配置注册告警与 RPC，主密钥由 ChainKeyProvider 读取。
        context.Services.AddSingleton<Chain.ChainAlertService>(sp => new Chain.ChainAlertService(
            Array.Empty<Guid>(),
            sp.GetRequiredService<Volo.Abp.EventBus.Distributed.IDistributedEventBus>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Chain.ChainAlertService>>()));
        context.Services.AddSingleton<Chain.ChainGatewayFactory>(sp => new Chain.ChainGatewayFactory(
            new System.Net.Http.HttpClient(Chain.ChainHttpTransport.CreateHandler()),
            sp.GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>(),
            Chain.AnkrBscChainGateway.DefaultEtherscanV2Endpoint,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            sp.GetRequiredService<IServiceScopeFactory>()));

        // API 密钥位于请求头或 URL 中，探测请求不进入 HTTP 日志及全局出站报文分析器。
        context.Services.AddHttpClient(Chain.ChainProviderProbe.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.MaxResponseContentBufferSize = 16 * 1024;
            })
            .RemoveAllLoggers()
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) =>
            {
                for (var i = handlers.Count - 1; i >= 0; i--)
                {
                    if (handlers[i] is ProfilerDelegatingHandler)
                    {
                        handlers.RemoveAt(i);
                    }
                }
            })
            .ConfigurePrimaryHttpMessageHandler(Chain.ChainHttpTransport.CreateHandler);

        Configure<AbpAuditingOptions>(options =>
        {
            options.IgnoredTypes.Add(typeof(Services.ConfigureAnkrInput));
            options.IgnoredTypes.Add(typeof(Services.AddNetworkApiKeyInput));
            options.IgnoredTypes.Add(typeof(Services.TestNodeCredentialInput));
        });

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
        context.Services.AddTransient<CollectionIntegrationService>();
        context.Services.AddTransient<ICollectionRefundService, Payments.CollectionRefundService>();

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
