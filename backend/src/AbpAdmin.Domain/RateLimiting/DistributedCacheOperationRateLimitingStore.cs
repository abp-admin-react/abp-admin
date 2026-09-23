using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 基于分布式缓存的操作限流计数存储。
/// 用 IAbpDistributedLock 协调递增（Redis:IsEnabled = true 时跨实例生效）。
/// 注意：Redis:IsEnabled = false 时锁退化为进程内 LocalAbpDistributedLock，多实例下限流阈值按实例数放大（见计划文档第 9 步）。
/// </summary>
public class DistributedCacheOperationRateLimitingStore : IOperationRateLimitingStore
{
    private readonly IDistributedCache<OperationRateLimitingCounterCacheItem> _cache;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly AbpAdminOperationRateLimitingOptions _options;

    public DistributedCacheOperationRateLimitingStore(
        IDistributedCache<OperationRateLimitingCounterCacheItem> cache,
        IAbpDistributedLock distributedLock,
        IOptions<AbpAdminOperationRateLimitingOptions> options)
    {
        _cache = cache;
        _distributedLock = distributedLock;
        _options = options.Value;
    }

    public async Task<OperationRateLimitingCounter> GetOrAddAsync(
        string cacheKey,
        TimeSpan windowDuration,
        TimeSpan cacheExpiration)
    {
        var item = await _cache.GetAsync(cacheKey);
        if (item != null)
        {
            // 兜底（问题2 修复）：逻辑窗口（ExpiresAt）已过但缓存项仍存活时按新窗口重置，
            // 避免 GetOrAdd 命中旧计数继续拒绝且 RetryAfter 为负。缓存 TTL 已锚定窗口终点，
            // 正常情况下到不了这里（时钟偏差/分布式缓存时钟不同步时的保护）。
            if (item.ExpiresAt.HasValue && item.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                await _cache.RemoveAsync(cacheKey);
            }
            else
            {
                return new OperationRateLimitingCounter
                {
                    Count = item.Count,
                    ExpiresAt = item.ExpiresAt
                };
            }
        }

        // 缓存未命中，初始化计数为 0（不递增，由 IncrementAsync 负责）
        var expiresAt = DateTimeOffset.UtcNow.Add(windowDuration);
        var newItem = new OperationRateLimitingCounterCacheItem(0, expiresAt);
        await _cache.SetAsync(
            cacheKey,
            newItem,
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                // 窗口锚定：缓存寿命对齐窗口终点（首次创建时二者等价，统一用绝对时间表达）
                AbsoluteExpiration = expiresAt
            });

        return new OperationRateLimitingCounter
        {
            Count = 0,
            ExpiresAt = expiresAt
        };
    }

    public async Task<OperationRateLimitingCounter> IncrementAsync(
        string cacheKey,
        TimeSpan windowDuration,
        TimeSpan cacheExpiration)
    {
        var lockKey = $"orl:lock:{cacheKey}";
        await using var handle = await _distributedLock.TryAcquireAsync(lockKey, _options.LockTimeout);
        if (handle == null)
        {
            // 锁超时是基础设施故障，抛基础设施异常，不要转成 429
            throw new AbpException($"获取操作限流分布式锁超时：{lockKey}");
        }

        var item = await _cache.GetAsync(cacheKey);
        if (item == null ||
            (item.ExpiresAt.HasValue && item.ExpiresAt.Value <= DateTimeOffset.UtcNow))
        {
            // 首次递增；或上个逻辑窗口已过期但缓存项仍残留（TTL 尚未到）——都从新窗口重新计数。
            // 原实现无条件 item.Count++，残留计数会让用户被多锁最长一个完整窗口。
            item = new OperationRateLimitingCounterCacheItem(1, DateTimeOffset.UtcNow.Add(windowDuration));
        }
        else
        {
            item.Count++;
        }

        await _cache.SetAsync(
            cacheKey,
            item,
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                // 窗口锚定（问题2 修复）：缓存寿命对齐窗口终点 ExpiresAt，不再随递增顺延。
                // 原实现用 AbsoluteExpirationRelativeToNow 每次递增都重臂一个完整窗口，
                // 计数项缓存寿命最长可达 2 倍窗口时长，导致固定窗口漂移 + RetryAfter 出负值。
                AbsoluteExpiration = item.ExpiresAt ?? DateTimeOffset.UtcNow.Add(cacheExpiration)
            });

        return new OperationRateLimitingCounter
        {
            Count = item.Count,
            ExpiresAt = item.ExpiresAt
        };
    }

    public async Task ResetAsync(string cacheKey)
    {
        await _cache.RemoveAsync(cacheKey);
    }
}
