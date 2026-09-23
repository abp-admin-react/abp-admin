using System;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using AbpAdmin.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Saas;

/// <summary>
/// T2.8 SaaS Pro 缺口第 4/6 项：租户连接字符串管理。
/// 断言要点：库里落密文（不是明文）、管理 API 返回掩码、留空保持原值、
/// 「使用共享数据库」删除记录、校验端点不写库、设置项禁用后更新被拒（业务异常）。
/// </summary>
public abstract class TenantConnectionStringTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string PlainConnectionString = "Data Source=:memory:;Cache=Shared";

    private readonly AbpAdmin.Tenants.TenantAppService _tenantAppService;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantManager _tenantManager;
    private readonly TenantConnectionStringProtector _protector;
    private readonly IConnectionStringResolver _connectionStringResolver;
    private readonly ICurrentTenant _currentTenant;
    private readonly ISettingManager _settingManager;

    protected TenantConnectionStringTests()
    {
        // ExposeServices 未 IncludeSelf：经基类解析，运行时实例就是我们的替换实现
        _tenantAppService = (AbpAdmin.Tenants.TenantAppService)GetRequiredService<Volo.Abp.TenantManagement.TenantAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
        _protector = GetRequiredService<TenantConnectionStringProtector>();
        _connectionStringResolver = GetRequiredService<IConnectionStringResolver>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _settingManager = GetRequiredService<ISettingManager>();
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

    [Fact]
    public void Should_Resolve_Our_TenantAppService()
    {
        // 验证 ABP 服务替换机制生效（基类 TenantAppService → 我们的实现）。
        // 注意 ITenantAppService 解析到的是 Castle 拦截代理，不能做类型断言；
        // 类类型解析返回的是继承本类的类代理，is 判断可以穿透。
        GetRequiredService<Volo.Abp.TenantManagement.TenantAppService>()
            .ShouldBeAssignableTo<AbpAdmin.Tenants.TenantAppService>();
    }

    [Fact]
    public async Task Should_Store_Encrypted_And_Return_Mask()
    {
        var tenantId = await CreateTenantAsync();

        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items =
            {
                new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString }
            }
        });

        // 直接查库看到的是密文，且能解密回原文
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        var stored = tenant.FindDefaultConnectionString();
        stored.ShouldNotBeNull();
        stored.ShouldNotBe(PlainConnectionString);
        _protector.DecryptOrPlain(stored).ShouldBe(PlainConnectionString);

        // 管理 API 返回掩码，永不返回明文
        var management = await _tenantAppService.GetConnectionStringsAsync(tenantId);
        management.IsManagementEnabled.ShouldBeTrue();
        management.UseSharedDatabase.ShouldBeFalse();
        management.Items.Count.ShouldBe(1);
        management.Items[0].Name.ShouldBe("Default");
        management.Items[0].Value.ShouldBe(TenantConnectionStringProtector.Mask);

        var defaultValue = await _tenantAppService.GetDefaultConnectionStringAsync(tenantId);
        defaultValue.ShouldBe(TenantConnectionStringProtector.Mask);
    }

    [Fact]
    public async Task Should_Keep_Value_When_Mask_Submitted()
    {
        var tenantId = await CreateTenantAsync();
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items = { new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString } }
        });

        // 回传掩码：保持原值
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items = { new TenantConnectionStringItemInput { Name = "Default", Value = TenantConnectionStringProtector.Mask } }
        });

        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        var stored = tenant.FindDefaultConnectionString();
        stored.ShouldNotBeNull();
        stored.ShouldNotBe(TenantConnectionStringProtector.Mask);
        _protector.DecryptOrPlain(stored).ShouldBe(PlainConnectionString);
    }

    [Fact]
    public async Task Should_Decrypt_When_Resolving_For_Connection_Use()
    {
        var tenantId = await CreateTenantAsync();
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items = { new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString } }
        });

        // 连接使用路径（MultiTenantConnectionStringResolver → TenantStore）拿到的是解密后的明文
        using (_currentTenant.Change(tenantId))
        {
            var resolved = await _connectionStringResolver.ResolveAsync();
            resolved.ShouldBe(PlainConnectionString);
        }
    }

    [Fact]
    public async Task Should_Remove_All_Records_When_UseSharedDatabase()
    {
        var tenantId = await CreateTenantAsync();
        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            Items =
            {
                new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString },
                new TenantConnectionStringItemInput { Name = "EasyAbpFileManagement", Value = PlainConnectionString }
            }
        });

        await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
        {
            UseSharedDatabase = true
        });

        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.ConnectionStrings.Count.ShouldBe(0);

        var management = await _tenantAppService.GetConnectionStringsAsync(tenantId);
        management.UseSharedDatabase.ShouldBeTrue();
        management.Items.Count.ShouldBe(0);

        // 回退到 host 的连接串
        using (_currentTenant.Change(tenantId))
        {
            var resolved = await _connectionStringResolver.ResolveAsync();
            resolved.ShouldNotBe(PlainConnectionString);
        }
    }

    [Fact]
    public async Task Should_Reject_Unknown_Database_Name()
    {
        var tenantId = await CreateTenantAsync();

        var exception = await Should.ThrowAsync<BusinessException>(async () =>
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "NotARealDatabase", Value = PlainConnectionString } }
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.InvalidTenantConnectionStringName);
    }

    [Fact]
    public async Task Should_List_Only_Databases_Used_By_Tenants()
    {
        var databases = await _tenantAppService.GetAvailableDatabasesAsync();

        // 项目只注册了 EasyAbpFileManagement 为 IsUsedByTenants；Default 由专门字段承载，不在列表里
        databases.ShouldContain("EasyAbpFileManagement");
        databases.ShouldNotContain("Default");
    }

    [Fact]
    public async Task Should_Check_Connection_String_Without_Persisting()
    {
        var tenantId = await CreateTenantAsync();

        // 合法的 SQLite 连接串（内存库，打开即成功）
        var ok = await _tenantAppService.CheckConnectionStringAsync(tenantId, new CheckTenantConnectionStringInput
        {
            ConnectionString = "Data Source=:memory:"
        });
        ok.IsValid.ShouldBeTrue();
        ok.ErrorMessage.ShouldBeNull();

        // 目录不存在的连接串：打开失败并返回具体原因
        var bad = await _tenantAppService.CheckConnectionStringAsync(tenantId, new CheckTenantConnectionStringInput
        {
            ConnectionString = "Data Source=/definitely-not-exists-t28-check/sub/x.db"
        });
        bad.IsValid.ShouldBeFalse();
        bad.ErrorMessage.ShouldNotBeNullOrWhiteSpace();

        // 纯校验，不写库：两次调用后租户仍无连接串记录
        var tenant = await WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(tenantId));
        tenant.ConnectionStrings.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Reject_Update_When_Management_Disabled()
    {
        var tenantId = await CreateTenantAsync();

        await _settingManager.SetGlobalAsync(
            AbpAdminSettings.Saas.EnableTenantBasedConnectionStringManagement, "false");

        var exception = await Should.ThrowAsync<BusinessException>(async () =>
            await _tenantAppService.UpdateConnectionStringsAsync(tenantId, new UpdateTenantConnectionStringsInput
            {
                Items = { new TenantConnectionStringItemInput { Name = "Default", Value = PlainConnectionString } }
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Tenants.TenantConnectionStringManagementDisabled);
    }
}
