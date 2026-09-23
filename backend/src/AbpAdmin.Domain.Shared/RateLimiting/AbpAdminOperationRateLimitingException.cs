using Volo.Abp;
using Volo.Abp.ExceptionHandling;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流异常。HTTP 状态码 429。
/// Data 字典包含以下键（前端与运维都依赖它们）：
/// PolicyName、MaxCount、CurrentCount、RemainingCount、RetryAfterSeconds、
/// RetryAfterMinutes、RetryAfter、WindowDurationSeconds、WindowDescription、RuleDetails。
///
/// 注意：OperationRateLimitingContext.ExtraProperties 里的每一项都会被
/// 复制进本异常的 Data 字典，而 ABP 的错误响应会把 Data 序列化返回给客户端。
/// 因此不要往 ExtraProperties 里放密钥或任何敏感信息。
/// </summary>
public class AbpAdminOperationRateLimitingException : BusinessException, IHasHttpStatusCode
{
    public int HttpStatusCode => 429;

    public AbpAdminOperationRateLimitingException(string code)
        : base(code)
    {
    }
}
