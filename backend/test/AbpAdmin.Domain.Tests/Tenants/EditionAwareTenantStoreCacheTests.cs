using System;
using System.Threading.Tasks;
using AbpAdmin.Editions;
using Shouldly;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Tenants;

/// <summary>
/// 验证 EditionAwareTenantStore 缓存写入缺陷已修复：
/// 在无 UoW 的上下文里触发一次租户解析，然后从缓存读回，断言 EditionId 非空。
/// </summary>
public abstract class EditionAwareTenantStoreCacheTests<TStartupModule> : AbpAdminDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITenantStore _tenantStore;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;
    private readonly IDistributedCache<TenantConfigurationCacheItem> _cache;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    protected EditionAwareTenantStoreCacheTests()
    {
        _tenantStore = GetRequiredService<ITenantStore>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
        _cache = GetRequiredService<IDistributedCache<TenantConfigurationCacheItem>>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    [Fact]
    public async Task Should_Cache_EditionId_Without_UnitOfWork()
    {
        // Arrange: 创建一个带版本的租户
        var editionId = Guid.NewGuid();
        var tenantName = "test-tenant-" + Guid.NewGuid().ToString("N")[..8];
        Guid tenantId = Guid.Empty;

        await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync(tenantName);
            tenant.SetProperty(EditionConsts.TenantEditionPropertyName, editionId.ToString());
            await _tenantRepository.InsertAsync(tenant);
            tenantId = tenant.Id;
        });

        // 清空缓存，确保下次解析时会重新填充
        var cacheKey = TenantConfigurationCacheItem.CalculateCacheKey(tenantId, null);
        await _cache.RemoveAsync(cacheKey);

        // Act: 在无 UoW 的上下文里触发租户解析（模拟中间件场景）
        // 注意：这里不能开 UoW，否则缓存写入会推迟到 UoW 完成
        var tenantConfig = await _tenantStore.FindAsync(tenantId);

        // Assert: 第一次解析（内存对象）应该包含 EditionId
        tenantConfig.ShouldNotBeNull();
        tenantConfig.EditionId.ShouldBe(editionId);

        // 再次从缓存读取，验证 EditionId 已正确写入缓存
        var cachedItem = await _cache.GetAsync(cacheKey);
        cachedItem.ShouldNotBeNull();
        cachedItem.Value.ShouldNotBeNull();
        cachedItem.Value.EditionId.ShouldBe(editionId);

        // 第三次调用，这次应该是缓存命中，验证缓存中的 EditionId 正确
        var cachedConfig = await _tenantStore.FindAsync(tenantId);
        cachedConfig.ShouldNotBeNull();
        cachedConfig.EditionId.ShouldBe(editionId);
    }
}
