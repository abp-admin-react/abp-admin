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
        // 有界化：归一化不缩长度（纯数字串照旧可 10KB），登录输入直通此处必须截断
        return Task.FromResult(OperationRateLimitingPartitionKeys.Bound(normalized));
    }
}
