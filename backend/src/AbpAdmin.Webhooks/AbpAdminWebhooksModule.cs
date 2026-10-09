using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Webhooks.Delivery;
using AbpAdmin.Webhooks.Localization;
using AbpAdmin.Webhooks.Permissions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Json;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Webhooks;

/// <summary>
/// 自包含 Webhook 模块（订阅 + 事件投递 + 发送记录），照 BizTemplate 模板接线：
/// 宿主/DbMigrator 各加一行 DependsOn 即生效；迁移随本工程（独立 History 表）。
/// 发布入口 <see cref="IWebhookPublisher"/>，投递执行 <see cref="Delivery.WebhookDeliveryJobArgs"/>
/// 走后台作业队列（宿主的 Quartz 承载）。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreModule), // EF 基建 + IAbpAdminDbSchemaMigrator 约定接口
    typeof(AbpAspNetCoreMvcModule),            // Auto API：ConventionalControllers
    typeof(AbpAuthorizationModule),            // 权限定义
    typeof(AbpLocalizationModule),
    typeof(AbpVirtualFileSystemModule),
    typeof(AbpJsonModule),
    typeof(AbpBackgroundJobsModule))]
public class AbpAdminWebhooksModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<WebhooksDbContext>(options =>
        {
            // Default 连接串（与框架同库多上下文）；Default 仓储含子实体（订阅事件按 Id 增删）
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        // 上下文级 UseNpgsql + 独立 History 表（运行期 PG-only；测试同为 PG、同款注册，
        // 仅注入 Testcontainers 容器连接串，不再有 provider 覆盖层）。
        // History 表 schema 从连接串 SearchPath 显式解析（见 WebhooksHistorySchemaResolver）：
        // 省略 schema 时 Npgsql 的 applied-migrations 查询不吃 search_path，
        // DbMigrator 写入的记账行宿主读不回，启动即 42P07 "already exists"
        // 连接串（含 SearchPath）供数据表落位；History 表 schema 显式传入（解析器）。
        // 背景：省略 schema 时 Npgsql 的 applied-migrations 查询不吃 search_path，
        // DbMigrator 写入的记账行宿主读不回，启动即 42P07 "already exists"
        var configuration = context.Services.GetConfiguration();
        var (historySchema, _) = EntityFrameworkCore.WebhooksHistorySchemaResolver.Resolve(
            configuration.GetConnectionString(AbpAdminWebhooksConsts.ConnectionStringName)
            ?? string.Empty);
        // 迁移器确保 History 表存在时用同一 schema（静态注入，仅迁移路径使用）
        Data.WebhooksDbSchemaMigrator.WebhooksHistorySchema = historySchema;
        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<WebhooksDbContext>(contextOptions =>
            {
                contextOptions.UseNpgsql(npgsql =>
                {
                    npgsql.MigrationsHistoryTable(AbpAdminWebhooksConsts.SchemaHistoryTable, historySchema);
                });
            });
        });

        // 显式注册迁移器：框架迁移循环按 IAbpAdminDbSchemaMigrator 接口枚举（模块接线契约）
        context.Services.AddTransient<IAbpAdminDbSchemaMigrator, Data.WebhooksDbSchemaMigrator>();

        // 投递专用命名 HttpClient
        context.Services.AddWebhookDeliveryHttpClient();

        // Auto API：/api/app/webhook-subscription、/api/app/webhook-send-record
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AbpAdminWebhooksModule).Assembly);
        });

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminWebhooksModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<WebhooksResource>("en")
                .AddVirtualJson("/Localization/Webhooks");
        });
    }
}
