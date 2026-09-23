using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Saas;

/// <summary>
/// 回归测试：运行时建租户的种子路径（TenantAppService.CreateAsync → IDataSeeder.SeedAsync
/// 全量贡献者）。修复前所有贡献者挤在同一未提交 UoW：框架 PermissionDataSeedContributor
/// 把全部已定义权限授给 admin 角色（插入未提交），T1 自研的 SettingUi/FileManagement/
/// DataScope/NotificationService 权限种子用 IPermissionDataSeeder「先查后插」查的是数据库、
/// 看不到未提交行，去重失效 → 同一 (TenantId, Name, ProviderName, ProviderKey) 插两条，
/// 请求末尾 SaveChanges 撞 AbpPermissionGrants 唯一索引，POST /api/multi-tenancy/tenants 500。
/// 修复（CreateAsync override：SeedInSeparateUow + RequiresNew，逐贡献者提交）后：
/// 连建两个租户都不抛异常、每个租户的授权无重复键、两租户授权行数一致。
/// </summary>
public abstract class TenantCreationSeedTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly Volo.Abp.TenantManagement.TenantAppService _tenantAppService;
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly ICurrentTenant _currentTenant;

    protected TenantCreationSeedTests()
    {
        // 与运行时一致：基类类型解析到 T2.8 的替换实现（CreateAsync override 在本类上）
        _tenantAppService = GetRequiredService<Volo.Abp.TenantManagement.TenantAppService>();
        _permissionGrantRepository = GetRequiredService<IPermissionGrantRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Should_Grant_Permissions_Without_Duplicates_When_Creating_Tenants()
    {
        // 修复前：第一次 CreateAsync 就在 UoW 完成时抛 DbUpdateException
        // （SQLite Error 19: UNIQUE constraint failed: AbpPermissionGrants...）
        var first = await _tenantAppService.CreateAsync(new TenantCreateDto
        {
            Name = "seedfix" + Guid.NewGuid().ToString("N")[..8],
            AdminEmailAddress = "admin1@example.com",
            AdminPassword = "1q2w3E*"
        });
        var second = await _tenantAppService.CreateAsync(new TenantCreateDto
        {
            Name = "seedfix" + Guid.NewGuid().ToString("N")[..8],
            AdminEmailAddress = "admin2@example.com",
            AdminPassword = "1q2w3E*"
        });

        var firstGrants = await GetTenantGrantsAsync(first.Id);
        var secondGrants = await GetTenantGrantsAsync(second.Id);

        // 授权完整且两个租户一致（框架贡献者授全部已定义权限，自研贡献者幂等跳过）
        firstGrants.Count.ShouldBeGreaterThan(0);
        secondGrants.Count.ShouldBe(firstGrants.Count);

        // 无重复授权键
        foreach (var grants in new[] { firstGrants, secondGrants })
        {
            grants.GroupBy(x => (x.Name, x.ProviderName, x.ProviderKey))
                .ShouldAllBe(group => group.Count() == 1);
        }
    }

    private async Task<List<PermissionGrant>> GetTenantGrantsAsync(Guid tenantId)
    {
        using (_currentTenant.Change(tenantId))
        {
            return await WithUnitOfWorkAsync(() => _permissionGrantRepository.GetListAsync());
        }
    }
}
