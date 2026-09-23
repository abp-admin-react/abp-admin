using System;
using System.Threading.Tasks;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 分布式 generation 实现：generation 值存 <c>IDistributedCache&lt;long&gt;</c>
/// （Redis 部署下所有实例共享同一计数，任一实例递增后其余实例下一次读取即拿到新值，
/// 用户级缓存键因 generation 前缀变化而整体失效——多实例扩容前必须启用本实现，
/// 否则实例 A 上的范围缩权在实例 B 上要等 5 分钟绝对过期才生效，属安全敏感缺陷）。
/// <para>Redis 关闭时 ABP 的分布式缓存自动回落进程内存，行为与
/// <see cref="LocalDataScopeCacheGeneration"/> 等价（单机场景无损），因此模块默认注册本实现。</para>
/// <para>递增用短分布式锁（TryAcquire，零等待）串行化「读-改-写」：ABP 的 IDistributedCache
/// 未暴露原子自增原语，裸的读-改-写在并发递增时可能互相覆写——最坏情形是覆写回退，
/// 让已失效用户级脏快照重新命中新的 generation 键、撑满整个 5 分钟绝对过期
///（比旧注释里「丢一次增量」更差，见审查 round1）。
/// 锁把这一最坏场景压到只剩「拿不到锁时的降级路径」：重读一次后写入，
/// 仍可能丢一次增量，但不会回退（写入值必然 ≥ 持锁方已写入的值+1 或与其并发竞争），
/// 且 generation 只需单调不减——失效合并的后果仅是本次变更提前生效。</para>
/// </summary>
public class DistributedDataScopeCacheGeneration : IDataScopeCacheGeneration, ITransientDependency
{
    /// <summary>generation 计数的缓存键（经 ABP 缓存键前缀隔离，无碰撞）。</summary>
    public const string GenerationCacheKey = "datascope:generation";

    /// <summary>递增串行化的分布式锁键（与缓存键同命名空间，短临界区：一读一写）。</summary>
    public const string IncrementLockKey = "datascope:generation:increment";

    private readonly IDistributedCache<DataScopeGenerationCacheItem> _counter;
    private readonly IAbpDistributedLock _distributedLock;

    public DistributedDataScopeCacheGeneration(
        IDistributedCache<DataScopeGenerationCacheItem> counter,
        IAbpDistributedLock distributedLock)
    {
        _counter = counter;
        _distributedLock = distributedLock;
    }

    public virtual async Task<long> GetAsync()
    {
        // 键不存在时返回 null，兜成 0（与「从未递增过」等价）
        var item = await _counter.GetAsync(GenerationCacheKey);
        return item?.Generation ?? 0;
    }

    public virtual async Task<long> IncrementAsync()
    {
        // 零等待抢锁：拿不到说明另一实例/请求正在递增，本方走降级路径，
        // 不悬挂等待（递增发生在本地事件处理里，阻塞会拖慢 UoW 完成）
        await using var handle = await _distributedLock.TryAcquireAsync(IncrementLockKey, TimeSpan.Zero);
        if (handle != null)
        {
            var current = await GetAsync();
            var next = current + 1;
            await _counter.SetAsync(GenerationCacheKey, new DataScopeGenerationCacheItem { Generation = next });
            return next;
        }

        // 降级路径：锁被占（罕见——仅两次范围变更恰好同刻递增）。重读一次大概率已能
        // 观察到持锁方的写入，在其基础上 +1；即便仍撞上写入前窗口，也只是丢一次增量
        // （两次失效合并），不会出现持锁场景下的「覆写回退」。
        var degradedCurrent = await GetAsync();
        var degradedNext = degradedCurrent + 1;
        await _counter.SetAsync(GenerationCacheKey, new DataScopeGenerationCacheItem { Generation = degradedNext });
        return degradedNext;
    }
}
