using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// <see cref="IOperationRateLimitingChecker"/> 的便捷扩展方法：只传 parameter 字符串。
/// </summary>
public static class OperationRateLimitingCheckerExtensions
{
    public static Task CheckAsync(this IOperationRateLimitingChecker checker, string policyName, string? parameter = null)
        => checker.CheckAsync(policyName, new OperationRateLimitingContext { Parameter = parameter });

    public static Task<bool> IsAllowedAsync(this IOperationRateLimitingChecker checker, string policyName, string? parameter = null)
        => checker.IsAllowedAsync(policyName, new OperationRateLimitingContext { Parameter = parameter });

    public static Task<OperationRateLimitingStatus> GetStatusAsync(this IOperationRateLimitingChecker checker, string policyName, string? parameter = null)
        => checker.GetStatusAsync(policyName, new OperationRateLimitingContext { Parameter = parameter });

    public static Task ResetAsync(this IOperationRateLimitingChecker checker, string policyName, string? parameter = null)
        => checker.ResetAsync(policyName, new OperationRateLimitingContext { Parameter = parameter });
}
