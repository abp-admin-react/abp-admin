using System;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Driver;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AbpAdmin.HealthChecks;

/// <summary>
/// ClickHouse 连接健康检查:执行 SELECT version() 验证连通性。
/// 仅在 ClickHouse 配置启用(IsUsable)时由 Host 注册,未启用时调用方无法解析本类。
/// 注意:/health-status 端点匿名可达且使用 NoExceptionDetails writer——
/// 异常对象传入 Unhealthy 仅供内部诊断,不会回显给调用方;描述文案不得携带版本号等拓扑信息。
/// </summary>
public class ClickHouseHealthCheck : IHealthCheck
{
    private readonly IClickHouseClient _client;

    public ClickHouseHealthCheck(IClickHouseClient client)
    {
        _client = client;
    }

    /// <summary>
    /// 执行一次 SELECT version():任何非空结果即视为健康(不校验版本内容)。
    /// 取消令牌透传给驱动:CH 半开/不可达时随健康检查框架的超时中断,不长时间占用检查协程。
    /// </summary>
    /// <param name="context">健康检查上下文(未使用失败状态定制)。</param>
    /// <param name="cancellationToken">由调用方/健康检查框架提供的取消令牌。</param>
    /// <returns>Healthy=连接可用;Unhealthy=查询失败、返回空或被取消。</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // 令牌经命名参数直达驱动的取消通道,超时/停机都能中断在途请求
            var version = await _client.ExecuteScalarAsync("SELECT version()", cancellationToken: cancellationToken);
            return version is not null
                ? HealthCheckResult.Healthy("Could connect to ClickHouse.")
                : HealthCheckResult.Unhealthy("ClickHouse query returned no result.");
        }
        catch (OperationCanceledException)
        {
            // 取消来源可能是健康检查超时,也可能是宿主停机,统一按"检查未完成"上报
            return HealthCheckResult.Unhealthy("ClickHouse health check was cancelled or timed out.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("Error when trying to connect to ClickHouse.", e);
        }
    }
}
