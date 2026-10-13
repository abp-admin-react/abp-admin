using Microsoft.Extensions.Options;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 默认策略提供者，从 AbpAdminOperationRateLimitingOptions 读取。
/// </summary>
public class DefaultOperationRateLimitingPolicyProvider : IOperationRateLimitingPolicyProvider
{
    private readonly AbpAdminOperationRateLimitingOptions _options;

    public DefaultOperationRateLimitingPolicyProvider(IOptions<AbpAdminOperationRateLimitingOptions> options)
    {
        _options = options.Value;
    }

    public OperationRateLimitingPolicy? GetPolicy(string policyName)
    {
        return _options.Policies.TryGetValue(policyName, out var policy) ? policy : null;
    }
}
