using System;
using AbpAdmin.ClickHouse;
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

        // T5：ES/ClickHouse 属辅助存储，按配置开关注册——未启用时不注册，避免健康面板误报。
        // 启用判定走 options.IsUsable 单头谓词(与模块注册/日志装配同一份),防止多处复制漂移
        var configuration = services.GetConfiguration();
        var elasticsearchOptions = configuration
            .GetSection(AbpAdminElasticsearchOptions.SectionName)
            .Get<AbpAdminElasticsearchOptions>() ?? new AbpAdminElasticsearchOptions();
        if (elasticsearchOptions.IsUsable)
        {
            healthChecksBuilder.AddCheck<ElasticsearchHealthCheck>("Elasticsearch", tags: new string[] { "storage" });
        }

        var clickHouseOptions = configuration
            .GetSection(AbpAdminClickHouseOptions.SectionName)
            .Get<AbpAdminClickHouseOptions>() ?? new AbpAdminClickHouseOptions();
        if (clickHouseOptions.IsUsable)
        {
            healthChecksBuilder.AddCheck<ClickHouseHealthCheck>("ClickHouse", tags: new string[] { "storage" });
        }

        services.ConfigureHealthCheckEndpoint("/health-status");

        var healthCheckUrl = configuration["App:HealthCheckUrl"];

        if (string.IsNullOrEmpty(healthCheckUrl))
        {
            healthCheckUrl = "/health-status";
        }

        var healthChecksUiBuilder = services.AddHealthChecksUI(settings =>
        {
            settings.AddHealthCheckEndpoint("AbpAdmin Health Status", configuration["App:HealthUiCheckUrl"] ?? healthCheckUrl);
        });

        // Set your HealthCheck UI Storage here
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
                endpointContext.Endpoints.MapHealthChecksUI(setupOption)
                    // 同 /health-status：UI 面板（/health-ui、/health-api）无认证集成，
                    // FallbackPolicy 下不豁免即 401（NoExceptionDetails 已防异常回显，维持既有匿名语义）。
                    .AllowAnonymous();
            });
        });

        return services;
    }
}
