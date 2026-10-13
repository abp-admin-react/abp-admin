using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流检查器。作用在应用/领域代码层，可按业务维度（当前用户、当前租户、
/// 某个方法参数值）分区限流，与 ASP.NET Core 内置的 HTTP 管道层 rate limiting 不冲突。
/// </summary>
public interface IOperationRateLimitingChecker
{
    /// <summary>
    /// 检查并递增计数，超限抛 <see cref="AbpAdminOperationRateLimitingException"/>。
    /// </summary>
    Task CheckAsync(string policyName, OperationRateLimitingContext context);

    /// <summary>
    /// 只读检查，不递增。供 UI 预检用。
    /// </summary>
    Task<bool> IsAllowedAsync(string policyName, OperationRateLimitingContext context);

    /// <summary>
    /// 返回详细状态，不递增。
    /// </summary>
    Task<OperationRateLimitingStatus> GetStatusAsync(string policyName, OperationRateLimitingContext context);

    /// <summary>
    /// 重置该分区的计数。
    /// </summary>
    Task ResetAsync(string policyName, OperationRateLimitingContext context);
}
