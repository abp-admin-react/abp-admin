using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流规则（算法）接口。本轮只实现固定窗口 FixedWindowRateLimitingRule。
/// 可实现此接口提供滑动窗口、令牌桶等自定义算法。
/// </summary>
public interface IOperationRateLimitingRule
{
    /// <summary>
    /// 只读检查该规则下当前分区是否允许通过，不递增计数。
    /// </summary>
    Task<OperationRateLimitingRuleResult> CheckAsync(OperationRateLimitingRuleContext context);

    /// <summary>
    /// 递增该规则下当前分区的计数。
    /// </summary>
    Task IncrementAsync(OperationRateLimitingRuleContext context);

    /// <summary>
    /// 获取该规则下当前分区的详细状态，不递增计数。
    /// </summary>
    Task<OperationRateLimitingStatus> GetStatusAsync(OperationRateLimitingRuleContext context);

    /// <summary>
    /// 重置该规则下当前分区的计数。
    /// </summary>
    Task ResetAsync(OperationRateLimitingRuleContext context);
}
