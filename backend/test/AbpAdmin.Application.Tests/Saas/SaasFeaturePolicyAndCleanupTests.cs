using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Editions;
using AbpAdmin.Features;
using AbpAdmin.Menus;
using AbpAdmin.Tenants;
using Microsoft.Data.Sqlite;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Saas;

/// <summary>
/// T2.8 SaaS Pro 缺口补齐的四条链路：
/// 1) FeatureManagement ProviderPolicies——租户/版本级功能编辑此前抛
///    "No policy defined to get/set permissions for the provider"（点开即 500）；
/// 2) 版本删除清理该版本功能值（AbpFeatureValues 不再留 "E" 孤儿行）；
/// 3) 租户删除清理功能值 + 菜单/授权；
/// 4) 默认连接串变更触发运行期建库+schema 迁移（独立库租户不再只能离线跑 DbMigrator），
///    覆盖两个写通道（OSS Default 通道 + 前端抽屉实际走的 Items 通道）。
/// 另含掩码守卫（含掩码字面量的连接串值必须被拒绝）。
/// </summary>
public abstract class SaasFeaturePolicyAndCleanupTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IFeatureAppService _featureAppService;
    private readonly IFeatureValueRepository _featureValueRepository;
    private readonly IFeatureManagementStore _featureManagementStore;
    private readonly IEditionAppService _editionAppService;
    private readonly AbpAdmin.Tenants.TenantAppService _tenantAppService;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;
    private readonly TenantConnectionStringProtector _connectionStringProtector;
    private readonly MenuManager _menuManager;
    private readonly IRepository<Menu, Guid> _menuRepository;
    private readonly ICurrentTenant _currentTenant;

    protected SaasFeaturePolicyAndCleanupTests()
    {
        _featureAppService = GetRequiredService<IFeatureAppService>();
        _featureValueRepository = GetRequiredService<IFeatureValueRepository>();
        _featureManagementStore = GetRequiredService<IFeatureManagementStore>();
        _editionAppService = GetRequiredService<IEditionAppService>();
        // ExposeServices 未 IncludeSelf：经基类解析，运行时实例就是我们的替换实现
        _tenantAppService = (AbpAdmin.Tenants.TenantAppService)GetRequiredService<Volo.Abp.TenantManagement.TenantAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
        _connectionStringProtector = GetRequiredService<TenantConnectionStringProtector>();
        _menuManager = GetRequiredService<MenuManager>();
        _menuRepository = GetRequiredService<IRepository<Menu, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private async Task<Guid> CreateTenantAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync("t" + Guid.NewGuid().ToString("N")[..12]);
            await _tenantRepository.InsertAsync(tenant);
            return tenant.Id;
        });
    }

    private async Task<Guid> CreateEditionAsync()
    {
        var dto = await _editionAppService.CreateAsync(
            new CreateEditionDto { DisplayName = "e" + Guid.NewGuid().ToString("N")[..8] });
        return dto.Id;
    }

    private Task<List<Volo.Abp.FeatureManagement.FeatureValue>>
        GetFeatureValuesAsync(string providerName, string providerKey)
    {
        return WithUnitOfWorkAsync(() => _featureValueRepository.GetListAsync(providerName, providerKey));
    }

    /// <summary>断言功能值已被清掉，且经 FeatureManagementStore（缓存优先）也读不到——
    /// 只断言仓储行会放过「删行但留脏缓存」的回归。</summary>
    private async Task Should_Be_Fully_Cleaned_Async(string name, string providerName, string providerKey)
    {
        (await GetFeatureValuesAsync(providerName, providerKey)).ShouldBeEmpty();

        var viaStore = await WithUnitOfWorkAsync(() =>
            _featureManagementStore.GetOrNullAsync(name, providerName, providerKey));
        viaStore.ShouldBeNull("清理后经缓存/仓储都不应再读到该功能值");
    }

    [Fact]
    public async Task Should_Edit_Features_For_Tenant_And_Edition()
    {
        var editionId = await CreateEditionAsync();
        var tenantId = await CreateTenantAsync();

        // 此前两条 Get 均抛 "No policy defined..."；策略映射后按权限放行（admin 已授予全部权限）
        (await _featureAppService.GetAsync(TenantFeatureValueProvider.ProviderName, tenantId.ToString()))
            .Groups.ShouldNotBeEmpty();
        (await _featureAppService.GetAsync(EditionFeatureValueProvider.ProviderName, editionId.ToString()))
            .Groups.ShouldNotBeEmpty();

        await _featureAppService.UpdateAsync(
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString(),
            new UpdateFeaturesDto
            {
                // 注意：10.6.1 的 UpdateFeaturesDto.Features 默认为 null（只有 NotNull 注解），
                // 必须显式 new；集合初始化器语法会往 null 上 Add 抛 NRE
                Features = new List<UpdateFeatureDto>
                {
                    new UpdateFeatureDto
                    {
                        Name = AbpAdminFeatures.FileManagementStorageQuotaBytes,
                        Value = "1024"
                    }
                }
            });

        var stored = await GetFeatureValuesAsync(TenantFeatureValueProvider.ProviderName, tenantId.ToString());
        stored.ShouldContain(v =>
            v.Name == AbpAdminFeatures.FileManagementStorageQuotaBytes && v.Value == "1024");
    }

    [Fact]
    public async Task Should_Clean_Feature_Values_And_Invalidate_Cache_When_Edition_Deleted()
    {
        var editionId = await CreateEditionAsync();
        const string featureName = AbpAdminFeatures.FileManagementStorageQuotaBytes;

        await _featureManagementStore.SetAsync(
            featureName,
            "2048",
            EditionFeatureValueProvider.ProviderName,
            editionId.ToString());

        var before = await GetFeatureValuesAsync(EditionFeatureValueProvider.ProviderName, editionId.ToString());
        before.ShouldContain(v => v.Name == featureName && v.Value == "2048");

        await _editionAppService.DeleteAsync(editionId, new DeleteEditionInput());

        // 清理处理器与删除同一 UoW：删除返回后孤儿行已清、缓存已失效
        await Should_Be_Fully_Cleaned_Async(
            featureName, EditionFeatureValueProvider.ProviderName, editionId.ToString());
    }

    [Fact]
    public async Task Should_Cleanup_Feature_Values_And_Menus_When_Tenant_Deleted()
    {
        await WithUnitOfWorkAsync(() => _menuManager.SeedHostTemplateAsync());
        var tenantId = await CreateTenantAsync();
        const string featureName = AbpAdminFeatures.FileManagementStorageQuotaBytes;

        // 给租户拷一份菜单（懒拷贝同路径），并挂一个功能值
        using (_currentTenant.Change(tenantId))
        {
            await WithUnitOfWorkAsync(() => _menuManager.EnsureTenantMenusAsync(tenantId));
            // 前置断言：拷贝确实产出了菜单——否则删除后的 ShouldBeEmpty 是空洞通过
            (await _menuRepository.GetListAsync(x => true)).ShouldNotBeEmpty();
        }

        await _featureManagementStore.SetAsync(
            featureName,
            "4096",
            TenantFeatureValueProvider.ProviderName,
            tenantId.ToString());
        var before = await GetFeatureValuesAsync(TenantFeatureValueProvider.ProviderName, tenantId.ToString());
        before.ShouldContain(v => v.Name == featureName && v.Value == "4096");

        await _tenantAppService.DeleteAsync(tenantId);

        await Should_Be_Fully_Cleaned_Async(
            featureName, TenantFeatureValueProvider.ProviderName, tenantId.ToString());

        using (_currentTenant.Change(tenantId))
        {
            var menus = await WithUnitOfWorkAsync(() => _menuRepository.GetListAsync(x => true));
            menus.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Should_Create_And_Migrate_Database_When_Default_ConnectionString_Changed()
    {
        var tenantId = await CreateTenantAsync();
        var dbPath = TempDbPath();
        try
        {
            // 事件经分布式总线缓冲到 UoW 提交后投递；LocalDistributedEventBus 同步执行，
            // 调用返回时建库+迁移已完成（失败会被处理器记日志吞掉，这里用产物断言兜住）
            await _tenantAppService.UpdateDefaultConnectionStringAsync(tenantId, $"Data Source={dbPath}");

            await Should_Have_Migrated_Schema_Async(dbPath);
        }
        finally
        {
            CleanupTempDb(dbPath);
        }
    }

    [Fact]
    public async Task Should_Create_And_Migrate_Database_Via_ConnectionStrings_Items_Channel()
    {
        // 前端连接串抽屉把 Default 作为 Items 一项经 UpdateConnectionStringsAsync 提交——
        // 这是真正的生产路径，必须同样触发建库+迁移（回归保护：触发条件曾漏挂在本通道）
        var tenantId = await CreateTenantAsync();
        var dbPath = TempDbPath();
        try
        {
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "Default", Value = $"Data Source={dbPath}" } }
            });

            await Should_Have_Migrated_Schema_Async(dbPath);
        }
        finally
        {
            CleanupTempDb(dbPath);
        }
    }

    [Fact]
    public async Task Should_Not_Create_Database_When_Using_Shared_Database()
    {
        // UseSharedDatabase=true 是「回退共享库」：不得触发建库迁移（负向条件保护）
        var tenantId = await CreateTenantAsync();
        var dbPath = TempDbPath();
        try
        {
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                UseSharedDatabase = true,
                Items = { new TenantConnectionStringItemInput { Name = "Default", Value = $"Data Source={dbPath}" } }
            });

            File.Exists(dbPath).ShouldBeFalse("使用共享数据库时不得建库");
        }
        finally
        {
            CleanupTempDb(dbPath);
        }
    }

    [Fact]
    public async Task Should_Reject_Mask_Containing_ConnectionString_Value()
    {
        // 守卫场景：值"包含"掩码字面量但不是纯掩码回传——此前会被当真值加密入库，
        // 解密出的「掩码+连接串」任何数据库驱动都解析不了
        var tenantId = await CreateTenantAsync();
        var bad = "Data Source=x.db" + TenantConnectionStringProtector.Mask;

        var exItems = await Should.ThrowAsync<BusinessException>(async () =>
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "Default", Value = bad } }
            }));
        exItems.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.InvalidTenantConnectionStringValue);

        var exDefault = await Should.ThrowAsync<BusinessException>(async () =>
            await _tenantAppService.UpdateDefaultConnectionStringAsync(tenantId, bad));
        exDefault.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.InvalidTenantConnectionStringValue);

        // 两个通道都必须拒绝且不落库（没有半提交）
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.FindDefaultConnectionString().ShouldBeNull();
    }

    private static string TempDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"abpadmin-test-tenant-{Guid.NewGuid():N}.db");
    }

    private static async Task Should_Have_Migrated_Schema_Async(string dbPath)
    {
        File.Exists(dbPath).ShouldBeTrue("迁移事件应在提交后同步完成建库");

        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AbpTenants'";
        ((long)(await command.ExecuteScalarAsync())!).ShouldBeGreaterThan(0);
    }

    private static void CleanupTempDb(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }
    }
}
