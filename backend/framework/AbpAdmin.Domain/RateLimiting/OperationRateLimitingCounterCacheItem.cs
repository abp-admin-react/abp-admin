using System;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流计数缓存项。
/// 注意：字段形状变更需参考 T1.5 缓存键版本段机制（当前字段极少且窗口短，风险可接受）。
/// [IgnoreMultiTenancy]：租户隔离由限流组件自己在 key 里管理（WithMultiTenancy() 加租户段），
/// 禁用 ABP 缓存层的自动租户前缀，避免双重隔离导致非 MultiTenancy 规则在租户间不共享计数。
/// </summary>
[Serializable]
[IgnoreMultiTenancy]
public class OperationRateLimitingCounterCacheItem
{
    /// <summary>当前计数。</summary>
    public int Count { get; set; }

    /// <summary>窗口过期时间（UTC）。</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public OperationRateLimitingCounterCacheItem()
    {
    }

    public OperationRateLimitingCounterCacheItem(int count, DateTimeOffset? expiresAt)
    {
        Count = count;
        ExpiresAt = expiresAt;
    }
}
