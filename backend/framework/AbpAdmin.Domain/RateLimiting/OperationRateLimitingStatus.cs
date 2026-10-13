using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流状态（只读，不递增计数）。
/// </summary>
public class OperationRateLimitingStatus
{
    public bool IsAllowed { get; set; }

    public int RemainingCount { get; set; }

    public TimeSpan? RetryAfter { get; set; }

    public int MaxCount { get; set; }

    public int CurrentCount { get; set; }
}
