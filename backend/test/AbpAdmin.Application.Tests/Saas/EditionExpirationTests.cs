using System;
using System.Threading.Tasks;
using AbpAdmin.Editions;
using AbpAdmin.Tenants;
using Shouldly;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Saas;

/// <summary>
/// T2.8 SaaS Pro 缺口：版本过期与版本缓存失效。
/// 链路（已核实 10.6 源码）：IFeatureChecker 的 EditionFeatureValueProvider
/// → ITenantStore.FindAsync(tenantId).EditionId → EditionAwareTenantStore.ApplyEdition（过期判定内联在此）。
/// 所以本测试在 TenantStore 层断言：过期后 TenantConfiguration.EditionId 为 null，
/// feature 解析自然回落；但 AbpTenants 上存的版本关联仍在。
/// </summary>
public abstract class EditionExpirationTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITenantStore _tenantStore;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;
    private readonly Volo.Abp.Domain.Repositories.IRepository<Edition, Guid> _editionRepository;
    private readonly IEditionAppService _editionAppService;
    private readonly IDistributedCache<TenantConfigurationCacheItem> _cache;

    protected EditionExpirationTests()
    {
        _tenantStore = GetRequiredService<ITenantStore>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
        _editionRepository = GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<Edition, Guid>>();
        _editionAppService = GetRequiredService<IEditionAppService>();
        _cache = GetRequiredService<IDistributedCache<TenantConfigurationCacheItem>>();
    }

    private async Task<Guid> CreateTenantWithEditionAsync(Guid editionId, DateTime? editionEndDateUtc)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync("t" + Guid.NewGuid().ToString("N")[..12]);
            tenant.SetProperty(EditionConsts.TenantEditionPropertyName, editionId.ToString());
            if (editionEndDateUtc.HasValue)
            {
                tenant.SetProperty(AbpAdminTenantConsts.EditionEndDateUtcPropertyName, editionEndDateUtc.Value);
            }

            await _tenantRepository.InsertAsync(tenant);
            return tenant.Id;
        });
    }

    private async Task SetEditionEndDateUtcAsync(Guid tenantId, DateTime? value)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantRepository.GetAsync(tenantId);
            if (value.HasValue)
            {
                tenant.SetProperty(AbpAdminTenantConsts.EditionEndDateUtcPropertyName, value.Value);
            }
            else
            {
                tenant.RemoveProperty(AbpAdminTenantConsts.EditionEndDateUtcPropertyName);
            }

            await _tenantRepository.UpdateAsync(tenant);
        });

        await _cache.RemoveAsync(TenantConfigurationCacheItem.CalculateCacheKey(tenantId, null));
    }

    [Fact]
    public async Task Should_Clear_Effective_Edition_When_Expired_But_Keep_Association()
    {
        var edition = await WithUnitOfWorkAsync(async () =>
            await _editionRepository.InsertAsync(new Edition(Guid.NewGuid(), "标准版-" + Guid.NewGuid().ToString("N")[..6])));

        var tenantId = await CreateTenantWithEditionAsync(edition.Id, DateTime.UtcNow.AddDays(30));

        // 未过期：有效版本 = 分配的版本（连续读两次，第二次是缓存命中）
        var config = await _tenantStore.FindAsync(tenantId);
        config.ShouldNotBeNull();
        config.EditionId.ShouldBe(edition.Id);
        var cachedConfig = await _tenantStore.FindAsync(tenantId);
        cachedConfig.ShouldNotBeNull();
        cachedConfig.EditionId.ShouldBe(edition.Id);

        // 把 EditionEndDateUtc 设为昨天并清缓存
        await SetEditionEndDateUtcAsync(tenantId, DateTime.UtcNow.AddDays(-1));

        // 过期后：有效版本为 null（feature 解析回落），第二次缓存命中同样为 null
        var expiredConfig = await _tenantStore.FindAsync(tenantId);
        expiredConfig.ShouldNotBeNull();
        expiredConfig.EditionId.ShouldBeNull();
        var expiredCachedConfig = await _tenantStore.FindAsync(tenantId);
        expiredCachedConfig.ShouldNotBeNull();
        expiredCachedConfig.EditionId.ShouldBeNull();

        // 但 AbpTenants 上存的版本关联仍保留原值（便于续费时恢复）
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.ExtraProperties.TryGetValue(EditionConsts.TenantEditionPropertyName, out var value).ShouldBeTrue();
        value?.ToString().ShouldBe(edition.Id.ToString());
    }

    [Fact]
    public async Task Should_Invalidate_Tenant_Cache_When_Edition_Changed()
    {
        var edition = await WithUnitOfWorkAsync(async () =>
            await _editionRepository.InsertAsync(new Edition(Guid.NewGuid(), "专业版-" + Guid.NewGuid().ToString("N")[..6])));
        var tenantId = await CreateTenantWithEditionAsync(edition.Id, null);

        // 预热缓存
        (await _tenantStore.FindAsync(tenantId)).ShouldNotBeNull();
        var cacheKey = TenantConfigurationCacheItem.CalculateCacheKey(tenantId, null);
        (await _cache.GetAsync(cacheKey)).ShouldNotBeNull();

        // 修改版本 → 关联租户缓存应立即失效（UoW 完成时应用删除）
        await WithUnitOfWorkAsync(async () =>
        {
            var tracked = await _editionRepository.GetAsync(edition.Id);
            tracked.SetDisplayName(tracked.DisplayName + "-v2");
            await _editionRepository.UpdateAsync(tracked);
        });

        (await _cache.GetAsync(cacheKey)).ShouldBeNull();
    }

    [Fact]
    public async Task Should_Move_Tenants_When_Deleting_Edition_With_Target()
    {
        Guid sourceId = Guid.Empty, targetId = Guid.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            sourceId = (await _editionRepository.InsertAsync(new Edition(Guid.NewGuid(), "源-" + Guid.NewGuid().ToString("N")[..6]))).Id;
            targetId = (await _editionRepository.InsertAsync(new Edition(Guid.NewGuid(), "目标-" + Guid.NewGuid().ToString("N")[..6]))).Id;
        });

        var tenantIds = new Guid[3];
        for (var i = 0; i < 3; i++)
        {
            tenantIds[i] = await CreateTenantWithEditionAsync(sourceId, null);
        }

        await _editionAppService.DeleteAsync(sourceId, new DeleteEditionInput { MoveTenantsToEditionId = targetId });

        foreach (var tenantId in tenantIds)
        {
            var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
            tenant.ExtraProperties.TryGetValue(EditionConsts.TenantEditionPropertyName, out var value).ShouldBeTrue();
            value?.ToString().ShouldBe(targetId.ToString());

            // 受影响租户的缓存已失效，重新解析后有效版本为迁移目标
            var config = await _tenantStore.FindAsync(tenantId);
            config.ShouldNotBeNull();
            config.EditionId.ShouldBe(targetId);
        }
    }

    [Fact]
    public async Task Should_Clear_Assignment_When_Deleting_Edition_Without_Target()
    {
        var edition = await WithUnitOfWorkAsync(async () =>
            await _editionRepository.InsertAsync(new Edition(Guid.NewGuid(), "待删-" + Guid.NewGuid().ToString("N")[..6])));
        var tenantId = await CreateTenantWithEditionAsync(edition.Id, null);

        await _editionAppService.DeleteAsync(edition.Id, new DeleteEditionInput());

        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.ExtraProperties.ContainsKey(EditionConsts.TenantEditionPropertyName).ShouldBeFalse();

        var config = await _tenantStore.FindAsync(tenantId);
        config.ShouldNotBeNull();
        config.EditionId.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Count_Tenants_Of_Edition()
    {
        var edition = await WithUnitOfWorkAsync(async () =>
            await _editionRepository.InsertAsync(new Edition(Guid.NewGuid(), "计数-" + Guid.NewGuid().ToString("N")[..6])));
        await CreateTenantWithEditionAsync(edition.Id, null);
        await CreateTenantWithEditionAsync(edition.Id, null);

        (await _editionAppService.GetTenantCountAsync(edition.Id)).ShouldBe(2);
    }
}
