using System.Threading.Tasks;
using Volo.Abp.Users;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// 优先 context.Parameter，为空回退 CurrentUser.Email；规范化 ToUpperInvariant() + 有界化截断。
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
        // 有界化：登录输入未经验证直通此处，不截断则任意超长输入都生成独立计数键（内存 DoS）
        return Task.FromResult(OperationRateLimitingPartitionKeys.Bound(value?.ToUpperInvariant()));
    }
}
