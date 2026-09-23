using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// 客户端 IP。依赖 IOperationRateLimitingClientIpProvider（由上层实现）。
/// </summary>
public class ClientIpOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    private readonly IOperationRateLimitingClientIpProvider _clientIpProvider;

    public ClientIpOperationRateLimitingPartitionKeyResolver(IOperationRateLimitingClientIpProvider clientIpProvider)
    {
        _clientIpProvider = clientIpProvider;
    }

    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        return Task.FromResult(_clientIpProvider.GetClientIpAddress());
    }
}
