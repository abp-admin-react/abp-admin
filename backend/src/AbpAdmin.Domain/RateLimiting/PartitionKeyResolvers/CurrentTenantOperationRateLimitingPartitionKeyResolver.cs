using System.Threading.Tasks;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// CurrentTenant.Id，host 用字符串 "host"。
/// </summary>
public class CurrentTenantOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    private readonly ICurrentTenant _currentTenant;

    public CurrentTenantOperationRateLimitingPartitionKeyResolver(ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
    }

    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        return Task.FromResult<string?>(_currentTenant.Id?.ToString() ?? "host");
    }
}
