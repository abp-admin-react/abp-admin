using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 单条规则执行时所需的上下文。
/// </summary>
public class OperationRateLimitingRuleContext
{
    /// <summary>限流上下文（含 Parameter 与 ExtraProperties）。</summary>
    public required OperationRateLimitingContext Context { get; set; }

    /// <summary>规则配置。</summary>
    public required OperationRateLimitingRuleConfig RuleConfig { get; set; }

    /// <summary>完整存储 key（已含 orl: 前缀、租户段、策略名、规则 key、分区键）。</summary>
    public required string CacheKey { get; set; }
}

/// <summary>
/// 单条规则的只读检查结果。
/// </summary>
public class OperationRateLimitingRuleResult
{
    public bool IsAllowed { get; set; }

    /// <summary>剩余次数。</summary>
    public int RemainingCount { get; set; }

    /// <summary>距窗口重置的等待时长。Ban 规则（maxCount=0）为 null。</summary>
    public TimeSpan? RetryAfter { get; set; }

    /// <summary>错误码。为空时用默认（超限 / Ban）。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>窗口的人类可读描述，如「每 1 小时最多 3 次」。Ban 规则为 null。</summary>
    public string? WindowDescription { get; set; }
}
