using System;
using AbpAdmin.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>
/// Webhook 模块测试的聚合模块：EF 测试基线 + Webhook 模块。
/// 连接串 / UoW 事务开关 / 静态权限特性不落库的口径都由所依赖的基线模块
/// （AbpAdminEntityFrameworkCoreTestModule，同程序集）提供——ABP 先配置依赖模块，
/// 这里不再重复接线；两个上下文同库（程序集专属 Testcontainers PG 库，各记各的迁移账），
/// 提供程序无需测试侧覆盖：模块注册的上下文级 UseNpgsql（独立 History 表）
/// 就是测试提供程序，运行期 PG-only 语义在测试里同构成立。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreTestModule),
    typeof(AbpAdminWebhooksModule))]
public class WebhooksTestsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Webhooks 上下文的表按模块同款独立 History 表（public schema，连接串无 SearchPath
        // 时与模块解析结果一致）真迁移建表——可能晚于基线上下文（首个 Webhooks 用例启动时），
        // AbpAdminTestPg 会在迁移后作废 Respawn 删除图
        var database = AbpAdminTestPg.GetDatabase(typeof(WebhooksTestsModule).Assembly);
        database.MigrateOnce("WebhooksDbContext", () =>
        {
            using var dbContext = new WebhooksDbContext(
                new DbContextOptionsBuilder<WebhooksDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(AbpAdminWebhooksConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });

        // Webhook 投递作业的出站 HTTP 替身（同探活的 RecordingHttpProbeHandler 惯例）：
        // 投递作业测试断言"SSRF 拦截路径零外呼"必须能看到请求有没有真的发起
        context.Services.AddSingleton<HttpStubs.RecordingWebhookDeliveryHandler>();
        context.Services.AddHttpClient(AbpAdmin.Webhooks.Delivery.WebhookDeliveryHttpClientExtensions.ClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<HttpStubs.RecordingWebhookDeliveryHandler>());
    }
}
