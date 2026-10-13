using System;
using System.Threading.Tasks;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Editions;

/// <summary>
/// 版本变更时使关联租户的 TenantConfigurationCacheItem 失效（T2.8 SaaS Pro 缺口第 3 项）。
/// 失效规则与 Pro 一致：
/// - 更新或删除租户 → 由开源 TenantConfigurationCacheItemInvalidator 负责（已有，不重复实现）；
/// - 更新或删除版本 → 本类负责，使所有「版本关联指向该版本」的租户缓存失效；
/// - EditionEndDateUtc 自然到期不主动失效（有意取舍，见 EditionAwareTenantStore.ApplyEdition 注释）。
/// </summary>
public class EditionChangedTenantCacheInvalidator :
    ILocalEventHandler<EntityChangedEventData<Edition>>,
    ITransientDependency
{
    private readonly EditionTenantManager _editionTenantManager;
    private readonly IDistributedCache<TenantConfigurationCacheItem> _cache;

    public EditionChangedTenantCacheInvalidator(
        EditionTenantManager editionTenantManager,
        IDistributedCache<TenantConfigurationCacheItem> cache)
    {
        _editionTenantManager = editionTenantManager;
        _cache = cache;
    }

    public virtual async Task HandleEventAsync(EntityChangedEventData<Edition> eventData)
    {
        // 新建版本时没有关联租户，无需失效
        if (eventData is EntityCreatedEventData<Edition>)
        {
            return;
        }

        var tenants = await _editionTenantManager.GetTenantsInEditionAsync(eventData.Entity.Id);
        foreach (var tenant in tenants)
        {
            await ClearCacheAsync(tenant.Id, tenant.NormalizedName);
        }
    }

    /// <summary>
    /// 与开源 TenantConfigurationCacheItemInvalidator 相同的键组合。
    /// considerUow: true 把实际删除推迟到 UoW 完成（事务提交之后），避免先清缓存后回滚留下脏缓存。
    /// </summary>
    protected virtual async Task ClearCacheAsync(Guid id, string normalizedName)
    {
        await _cache.RemoveManyAsync(new[]
        {
            TenantConfigurationCacheItem.CalculateCacheKey(id, null),
            TenantConfigurationCacheItem.CalculateCacheKey(null, normalizedName),
            TenantConfigurationCacheItem.CalculateCacheKey(id, normalizedName)
        }, considerUow: true);
    }
}
