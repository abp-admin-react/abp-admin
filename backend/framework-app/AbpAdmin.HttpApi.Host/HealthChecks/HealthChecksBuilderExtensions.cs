using System;
using AbpAdmin.Elasticsearch;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.HealthChecks;

public static class HealthChecksBuilderExtensions
{
    public static void AddAbpAdminHealthChecks(this IServiceCollection services)
    {
        // Add your health checks here
        var healthChecksBuilder = services.AddHealthChecks();
        healthChecksBuilder.AddCheck<AbpAdminDatabaseCheck>("AbpAdmin DbContext Check", tags: new string[] { "database" });

        // T5：ES 属辅助存储，按配置开关注册——未启用时不注册，避免健康面板误报。
        // 启用判定走 options.IsUsable 单头谓词(与模块注册/日志装配同一份),防止多处复制漂移
        var configuration = services.GetConfiguration();
        var elasticsearchOptions = configuration
            .GetSection(AbpAdminElasticsearchOptions.SectionName)
            .Get<AbpAdminElasticsearchOptions>() ?? new AbpAdminElasticsearchOptions();
        if (elasticsearchOptions.IsUsable)
        {
            healthChecksBuilder.AddCheck<ElasticsearchHealthCheck>("Elasticsearch", tags: new string[] { "storage" });
        }

        services.ConfigureHealthCheckEndpoint("/health-status");

        var healthCheckUrl = configuration["App:HealthCheckUrl"];

        if (string.IsNullOrEmpty(healthCheckUrl))
        {
            healthCheckUrl = "/health-status";
        }

        var healthChecksUiBuilder = services.AddHealthChecksUI(settings =>
        {
            // UI 登记的轮询端点必须是绝对 URI，不能沿用上面兜底出的相对路径：
            // HealthChecks.UI 会把它拼到 Kestrel 绑定地址上解析成 https://[::]:44395
            // （通配地址），自轮询每 10s 抛 "GetHealthReport threw an exception ...
            // 0.0.0.0/::0 are unspecified addresses that cannot be used as a target"。
            // 部署侧用 App:HealthUiCheckUrl 覆盖（docker-compose host 服务指
            // http://127.0.0.1:8080/health-status，与 compose 健康探针同端点）
            settings.AddHealthCheckEndpoint("AbpAdmin Health Status", configuration["App:HealthUiCheckUrl"] ?? healthCheckUrl);
        });

        // UI 历史存储：进程内 InMemory 够本地/单实例用（compose 健康探针只打
        // /health-status 端点，不消费 /health-api 的历史）；多实例部署或需要跨重启
        // 保留历史时再换持久化实现
        healthChecksUiBuilder.AddInMemoryStorage();

        services.MapHealthChecksUiEndpoints(options =>
        {
            options.UIPath = "/health-ui";
            options.ApiPath = "/health-api";
        });
    }

    private static IServiceCollection ConfigureHealthCheckEndpoint(this IServiceCollection services, string path)
    {
        services.Configure<AbpEndpointRouterOptions>(options =>
        {
            options.EndpointConfigureActions.Add(endpointContext =>
            {
                endpointContext.Endpoints.MapHealthChecks(
                    new PathString(path.EnsureStartsWith('/')),
                    new HealthCheckOptions
                    {
                        Predicate = _ => true,
                        // NoExceptionDetails:该端点匿名可达,失败时不把原始异常(含内部 URL/SQL)回显给未认证调用方
                        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponseNoExceptionDetails,
                        AllowCachingResponses = false,
                    })
                    // 匿名是探针面板的既有语义（k8s/lb 探活不带凭据）。
                    // 宿主启用 FallbackPolicy 默认拒绝后必须显式豁免，否则全部 401。
                    .AllowAnonymous();
            });
        });

        return services;
    }

    private static IServiceCollection MapHealthChecksUiEndpoints(this IServiceCollection services, Action<global::HealthChecks.UI.Configuration.Options>? setupOption = null)
    {
        services.Configure<AbpEndpointRouterOptions>(routerOptions =>
        {
            routerOptions.EndpointConfigureActions.Add(endpointContext =>
            {
                // AllowAnonymous 只保留给 /health-status 探针端点
                // （k8s/lb 探活语义，见 ConfigureHealthCheckEndpoint）；UI 面板整组
                // （/health-ui 面板、/health-api 执行历史 API、/healthchecks-webhooks
                // 通知注册管理面）不再匿名——FallbackPolicy 恢复为要求登录（健康面板本就
                // 应由已认证操作者查看；webhooks 注册是可触发宿主外呼的可写面）。
                // 匿名可达范围由 nginx 转发清单与宿主绑定为部署侧兜底。
                endpointContext.Endpoints.MapHealthChecksUI(setupOption);
            });
        });

        return services;
    }
}
