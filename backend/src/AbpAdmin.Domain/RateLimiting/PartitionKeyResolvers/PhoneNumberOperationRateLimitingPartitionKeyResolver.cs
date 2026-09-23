using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Users;

namespace AbpAdmin.RateLimiting.PartitionKeyResolvers;

/// <summary>
/// 优先 context.Parameter，为空回退 CurrentUser.PhoneNumber；剥离空格/短横线/点/圆括号，保留 + 与数字。
/// </summary>
public class PhoneNumberOperationRateLimitingPartitionKeyResolver : IOperationRateLimitingPartitionKeyResolver
{
    private readonly ICurrentUser _currentUser;

    public PhoneNumberOperationRateLimitingPartitionKeyResolver(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public Task<string?> ResolveAsync(OperationRateLimitingContext context)
    {
        var value = context.Parameter;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = _currentUser.PhoneNumber;
        }
        if (value == null)
        {
            return Task.FromResult<string?>(null);
        }
        // 剥离空格/短横线/点/圆括号，保留 + 与数字
        var normalized = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray());
        return Task.FromResult<string?>(normalized);
    }
}
