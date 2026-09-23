using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AbpAdmin.HealthChecks;

/// <summary>
/// Elasticsearch 连接健康检查:执行 Ping 验证集群可达性。
/// 仅在 Elasticsearch 配置启用(IsUsable)时由 Host 注册,未启用时调用方无法解析本类。
/// 注意:/health-status 端点匿名可达且使用 NoExceptionDetails writer——
/// 异常对象传入 Unhealthy 仅供内部诊断,不会回显给调用方。
/// ES 客户端 9.x 未提供接口抽象,这里依赖具体类 ElasticsearchClient(与模块注册一致)。
/// </summary>
public class ElasticsearchHealthCheck : IHealthCheck
{
    private readonly ElasticsearchClient _client;

    public ElasticsearchHealthCheck(ElasticsearchClient client)
    {
        _client = client;
    }

    /// <summary>
    /// 执行一次 Ping:响应有效即视为健康。取消令牌透传给客户端,随健康检查框架的超时中断。
    /// </summary>
    /// <param name="context">健康检查上下文(未使用失败状态定制)。</param>
    /// <param name="cancellationToken">由调用方/健康检查框架提供的取消令牌。</param>
    /// <returns>Healthy=Ping 通过;Unhealthy=Ping 未通过或连接失败。</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.PingAsync(cancellationToken: cancellationToken);
            return response.IsValidResponse
                ? HealthCheckResult.Healthy("Could connect to Elasticsearch.")
                : HealthCheckResult.Unhealthy("Elasticsearch ping failed.");
        }
        catch (OperationCanceledException)
        {
            // 取消来源可能是健康检查超时,也可能是宿主停机,统一按"检查未完成"上报
            return HealthCheckResult.Unhealthy("Elasticsearch health check was cancelled or timed out.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("Error when trying to connect to Elasticsearch.", e);
        }
    }
}
