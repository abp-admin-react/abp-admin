using System.Threading.Tasks;
using Volo.Abp.Users;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// CurrentUser.Id，未登录时为 "anonymous"。
/// </summary>
public class CurrentUserOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    private readonly ICurrentUser _currentUser;

    public CurrentUserOperationRateLimitingPartitionKeyResolver(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        return Task.FromResult<string?>(_currentUser.Id?.ToString() ?? "anonymous");
    }
}
