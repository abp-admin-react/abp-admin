using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// 直接用 context.Parameter 的值，仅做有界化截断（Parameter 通常是登录输入，无界）。
/// </summary>
public class ParameterOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        return Task.FromResult(OperationRateLimitingPartitionKeys.Bound(context.Parameter));
    }
}
