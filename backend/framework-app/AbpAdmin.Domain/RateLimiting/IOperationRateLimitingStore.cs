using System;
using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流计数存储后端。默认实现基于 IDistributedCache。
/// 可实现此接口替换存储后端。
/// </summary>
public interface IOperationRateLimitingStore
{
    /// <summary>
    /// 只读获取或初始化计数，不递增。
    /// </summary>
    Task<OperationRateLimitingCounter> GetOrAddAsync(string cacheKey, TimeSpan windowDuration, TimeSpan cacheExpiration);

    /// <summary>
    /// 递增计数并返回最新值。若是窗口内首次，则建立计数并设置过期时间为窗口时长。
    /// 通过分布式锁协调并发递增。
    /// </summary>
    Task<OperationRateLimitingCounter> IncrementAsync(string cacheKey, TimeSpan windowDuration, TimeSpan cacheExpiration);

    /// <summary>
    /// 重置（删除）该 key 的计数。
    /// </summary>
    Task ResetAsync(string cacheKey);
}

/// <summary>
/// 限流计数快照。
/// </summary>
public class OperationRateLimitingCounter
{
    /// <summary>当前窗口内已计数次数。</summary>
    public int Count { get; set; }

    /// <summary>窗口过期时间（UTC）。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
