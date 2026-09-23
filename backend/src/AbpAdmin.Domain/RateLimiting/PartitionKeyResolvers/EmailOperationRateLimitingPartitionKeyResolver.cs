using System.Threading.Tasks;
using Volo.Abp.Users;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// 优先 context.Parameter，为空回退 CurrentUser.Email；规范化 ToUpperInvariant()。
/// </summary>
public class EmailOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    private readonly ICurrentUser _currentUser;

    public EmailOperationRateLimitingPartitionKeyResolver(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        var value = context.Parameter;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = _currentUser.Email;
        }
        return Task.FromResult(value?.ToUpperInvariant());
    }
}
