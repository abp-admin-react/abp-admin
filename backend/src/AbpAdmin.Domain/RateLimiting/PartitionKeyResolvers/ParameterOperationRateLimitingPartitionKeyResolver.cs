using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// 直接用 context.Parameter 的值，原样不做任何规范化。
/// </summary>
public class ParameterOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        return Task.FromResult(context.Parameter);
    }
}
