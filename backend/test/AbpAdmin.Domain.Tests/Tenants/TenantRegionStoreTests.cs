using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Tenants;

/// <summary>
/// 钉住多机房租户归属契约（framework-contracts §7 / docs/dr-runbook.md §6）：
/// 1) 归属经租户实体 ExtraProperties 持久化往返（方案二按租户分区搬迁时的数据依据）；
/// 2) 未标注租户读侧回落 DefaultRegion——存量租户零迁移，方案一期间语义上全归主库机房。
/// 注意：归属只存实体，不进 TenantConfiguration（ABP 10.6 该类型无 ExtraProperties）；
/// 方案二运行期路由的读取点是每租户连接串，不走本标记。
/// </summary>
public abstract class TenantRegionStoreTests<TStartupModule> : AbpAdminDomainTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;

    protected TenantRegionStoreTests()
    {
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
    }

    [Fact]
    public async Task Should_Roundtrip_Region_Via_Repository()
    {
        var tenantName = "region-tenant-" + Guid.NewGuid().ToString("N")[..8];
        Guid tenantId = Guid.Empty;

        await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync(tenantName);
            tenant.SetRegion("B");
            await _tenantRepository.InsertAsync(tenant);
            tenantId = tenant.Id;
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var loaded = await _tenantRepository.FindAsync(tenantId);
            loaded.ShouldNotBeNull();
            loaded.GetRegion().ShouldBe("B");
        });
    }

    [Fact]
    public async Task Should_Fall_Back_To_Default_Region_When_Not_Stamped()
    {
        var tenantName = "default-region-tenant-" + Guid.NewGuid().ToString("N")[..8];
        Tenant tenant = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            // TenantManager 不打标——打标是 TenantAppService.CreateAsync 的职责，这里刻意裸建
            tenant = await _tenantManager.CreateAsync(tenantName);
        });

        // 未标注：读侧回落默认归属
        tenant.GetRegion().ShouldBe(AbpAdminTenantConsts.DefaultRegion);

        // 显式标注后读回标注值
        tenant.SetRegion("B");
        tenant.GetRegion().ShouldBe("B");
    }
}
