using System;
using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 固定窗口限流规则。窗口内计数达到 MaxCount 后拒绝，窗口过期后计数自动重置（由缓存过期实现）。
/// MaxCount = 0 表示 Ban（永久拒绝，RetryAfter 为 null）。
/// </summary>
public class FixedWindowRateLimitingRule : IOperationRateLimitingRule
{
    private readonly IOperationRateLimitingStore _store;
    private readonly IOperationRateLimitingFormatter _formatter;

    public FixedWindowRateLimitingRule(
        IOperationRateLimitingStore store,
        IOperationRateLimitingFormatter formatter)
    {
        _store = store;
        _formatter = formatter;
    }

    public async Task<OperationRateLimitingRuleResult> CheckAsync(OperationRateLimitingRuleContext context)
    {
        // Ban 规则：MaxCount = 0 直接拒绝
        if (context.RuleConfig.MaxCount == 0)
        {
            return CreateDeniedResult(context, currentCount: 0, retryAfter: null);
        }

        var counter = await _store.GetOrAddAsync(
            context.CacheKey,
            context.RuleConfig.Duration,
            context.RuleConfig.Duration);

        if (counter.Count >= context.RuleConfig.MaxCount)
        {
            var retryAfter = counter.ExpiresAt.HasValue
                ? counter.ExpiresAt.Value - DateTimeOffset.UtcNow
                : (TimeSpan?)null;
            // 问题2 修复：窗口已过期（缓存残留/时钟偏差）时不再给出负的重试等待
            if (retryAfter <= TimeSpan.Zero)
            {
                retryAfter = null;
            }
            return CreateDeniedResult(context, counter.Count, retryAfter);
        }

        return new OperationRateLimitingRuleResult
        {
            IsAllowed = true,
            RemainingCount = context.RuleConfig.MaxCount - counter.Count
        };
    }

    public async Task IncrementAsync(OperationRateLimitingRuleContext context)
    {
        // Ban 规则不递增
        if (context.RuleConfig.MaxCount == 0)
        {
            return;
        }

        await _store.IncrementAsync(
            context.CacheKey,
            context.RuleConfig.Duration,
            context.RuleConfig.Duration);
    }

    public async Task<OperationRateLimitingStatus> GetStatusAsync(OperationRateLimitingRuleContext context)
    {
        if (context.RuleConfig.MaxCount == 0)
        {
            return new OperationRateLimitingStatus
            {
                IsAllowed = false,
                RemainingCount = 0,
                RetryAfter = null
            };
        }

        var counter = await _store.GetOrAddAsync(
            context.CacheKey,
            context.RuleConfig.Duration,
            context.RuleConfig.Duration);

        var isAllowed = counter.Count < context.RuleConfig.MaxCount;
        var retryAfter = !isAllowed && counter.ExpiresAt.HasValue
            ? counter.ExpiresAt.Value - DateTimeOffset.UtcNow
            : (TimeSpan?)null;
        // 问题2 修复：窗口已过期（缓存残留/时钟偏差）时不再给出负的重试等待
        if (retryAfter <= TimeSpan.Zero)
        {
            retryAfter = null;
        }

        return new OperationRateLimitingStatus
        {
            IsAllowed = isAllowed,
            RemainingCount = Math.Max(0, context.RuleConfig.MaxCount - counter.Count),
            RetryAfter = retryAfter
        };
    }

    public Task ResetAsync(OperationRateLimitingRuleContext context)
    {
        return _store.ResetAsync(context.CacheKey);
    }

    private OperationRateLimitingRuleResult CreateDeniedResult(
        OperationRateLimitingRuleContext context,
        int currentCount,
        TimeSpan? retryAfter)
    {
        var isBan = context.RuleConfig.MaxCount == 0;
        return new OperationRateLimitingRuleResult
        {
            IsAllowed = false,
            ErrorCode = context.RuleConfig.ErrorCode
                ?? (isBan
                    ? AbpAdminDomainErrorCodes.RateLimiting.OperationBanned
                    : AbpAdminDomainErrorCodes.RateLimiting.OperationRateLimitExceeded),
            RemainingCount = 0,
            RetryAfter = retryAfter,
            WindowDescription = isBan
                ? null
                : _formatter.FormatWindowDescription(context.RuleConfig.Duration, context.RuleConfig.MaxCount)
        };
    }
}
