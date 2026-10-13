using System;
using System.Threading.Tasks;
using AbpAdmin.Editions;
using JetBrains.Annotations;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.ObjectMapping;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Tenants;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ITenantStore), typeof(TenantStore))]
public class EditionAwareTenantStore : TenantStore
{
    private readonly TenantConnectionStringProtector _connectionStringProtector;

    public EditionAwareTenantStore(
        ITenantRepository tenantRepository,
        IObjectMapper<AbpTenantManagementDomainModule> objectMapper,
        ICurrentTenant currentTenant,
        IDistributedCache<TenantConfigurationCacheItem> cache,
        TenantConnectionStringProtector connectionStringProtector)
        : base(tenantRepository, objectMapper, currentTenant, cache)
    {
        _connectionStringProtector = connectionStringProtector;
    }

    protected override async Task<TenantConfigurationCacheItem> SetCacheAsync(
        string cacheKey,
        [CanBeNull] Tenant tenant)
    {
        var cacheItem = BuildCacheItem(tenant);
        await Cache.SetAsync(cacheKey, cacheItem, considerUow: true);
        return cacheItem;
    }

    [Obsolete]
    protected override TenantConfigurationCacheItem SetCache(string cacheKey, [CanBeNull] Tenant tenant)
    {
        var cacheItem = BuildCacheItem(tenant);
        Cache.Set(cacheKey, cacheItem, considerUow: true);
        return cacheItem;
    }

    /// <summary>
    /// 「映射 + 应用版本 + 解密连接串 + 包缓存项」的公共实现（修复缓存写入缺陷：先改后写，
    /// 不复用基类的写入——基类 SetCacheAsync 内部已经调了 Cache.SetAsync，之后才写 EditionId
    /// 会导致无 UoW 时 EditionId 根本没进缓存）。同步/异步两个 override 都只做写缓存，
    /// 公共逻辑收敛在此防止单边漏改（历史上正是异步版漏写 EditionId 的缓存缺陷）。
    /// </summary>
    private TenantConfigurationCacheItem BuildCacheItem([CanBeNull] Tenant tenant)
    {
        var configuration = tenant != null ? ObjectMapper.Map<Tenant, TenantConfiguration>(tenant) : null;
        ApplyEdition(configuration, tenant); // 先改
        DecryptConnectionStrings(configuration);
        return new TenantConfigurationCacheItem(configuration); // 再由调用方写
    }

    private static void ApplyEdition(TenantConfiguration? configuration, Tenant? tenant)
    {
        if (configuration == null || tenant == null)
        {
            return;
        }

        if (!tenant.ExtraProperties.TryGetValue(EditionConsts.TenantEditionPropertyName, out var value) ||
            !Guid.TryParse(value?.ToString(), out var editionId))
        {
            return;
        }

        // T2.8：版本过期判定内联在这里，这是唯一接入点。
        // 过期后不设置 configuration.EditionId（保持 null），feature 解析自然回落；
        // 但 Tenant 上存的版本关联仍然保留（便于续费时恢复），不会去动它。
        // 注意：自然到期不主动清缓存，最长要等 TenantConfigurationCacheItem 的
        // 绝对过期周期（AbpAdminEditionCacheOptions，默认 1 小时）才生效——
        // 这是与 Pro 一致的有意取舍，否则每次请求都要查库比较时间，代价过高。
        // 需要立即生效时，管理员改一次租户（或版本）即可触发主动失效。
        var endDate = tenant.GetProperty<DateTime?>(AbpAdminTenantConsts.EditionEndDateUtcPropertyName);
        if (endDate.HasValue && endDate.Value < DateTime.UtcNow)
        {
            return;
        }

        configuration.EditionId = editionId;
    }

    /// <summary>
    /// 数据库中存的租户连接串是密文（见 TenantConnectionStringProtector），
    /// 构建 TenantConfiguration 时解密，供 MultiTenantConnectionStringResolver 直接使用。
    /// 注意：解密后的明文会随 TenantConfigurationCacheItem 进入分布式缓存——
    /// 这与 Pro 的威胁模型一致（Pro 连数据库里都是明文），本项目的改进点是
    /// 「数据库落盘为密文 + 管理 API 不返回明文」；若连缓存层也要求密文需要另行设计。
    /// </summary>
    private void DecryptConnectionStrings(TenantConfiguration? configuration)
    {
        if (configuration?.ConnectionStrings == null)
        {
            return;
        }

        foreach (var pair in configuration.ConnectionStrings)
        {
            if (pair.Value != null)
            {
                configuration.ConnectionStrings[pair.Key] = _connectionStringProtector.DecryptOrPlain(pair.Value);
            }
        }
    }
}
