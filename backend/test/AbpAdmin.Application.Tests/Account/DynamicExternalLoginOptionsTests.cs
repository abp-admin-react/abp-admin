using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Account;

/* T2.7 外部登录每租户动态配置集成测试。
 * 覆盖：两个租户各配一套不同的 GitHub 凭据，各自解析出各自值（租户隔离）；
 * ClientSecret 在数据库里是密文（SettingDefinition 标记 isEncrypted），读出时为明文。
 * Host 层的 DynamicExternalLoginOptionsManager 只是读取核心的薄包装。
 */
public abstract class DynamicExternalLoginOptionsTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ExternalLoginSettingsManager _externalLoginSettingsManager;
    private readonly ISettingManager _settingManager;
    private readonly ISettingRepository _settingRepository;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;

    protected DynamicExternalLoginOptionsTests()
    {
        _externalLoginSettingsManager = GetRequiredService<ExternalLoginSettingsManager>();
        _settingManager = GetRequiredService<ISettingManager>();
        _settingRepository = GetRequiredService<ISettingRepository>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private async Task<Guid> CreateTenantAsync(string name)
    {
        var tenant = await _tenantManager.CreateAsync(name);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });
        return tenant.Id;
    }

    [Fact]
    public async Task Tenant_scoped_github_credentials_are_isolated()
    {
        var tenantAId = await CreateTenantAsync($"ext-a-{Guid.NewGuid():N}".Substring(0, 20));
        var tenantBId = await CreateTenantAsync($"ext-b-{Guid.NewGuid():N}".Substring(0, 20));

        await _settingManager.SetForTenantAsync(tenantAId, "AbpAdmin.Account.ExternalLogin.GitHub.Enabled", "true");
        await _settingManager.SetForTenantAsync(tenantAId, "AbpAdmin.Account.ExternalLogin.GitHub.ClientId", "tenant-a-client-id");
        await _settingManager.SetForTenantAsync(tenantAId, "AbpAdmin.Account.ExternalLogin.GitHub.ClientSecret", "tenant-a-secret");

        await _settingManager.SetForTenantAsync(tenantBId, "AbpAdmin.Account.ExternalLogin.GitHub.Enabled", "true");
        await _settingManager.SetForTenantAsync(tenantBId, "AbpAdmin.Account.ExternalLogin.GitHub.ClientId", "tenant-b-client-id");
        await _settingManager.SetForTenantAsync(tenantBId, "AbpAdmin.Account.ExternalLogin.GitHub.ClientSecret", "tenant-b-secret");

        using (_currentTenant.Change(tenantAId))
        {
            var settingsA = await _externalLoginSettingsManager.GetProviderSettingsAsync("GitHub");
            settingsA.Enabled.ShouldBeTrue();
            settingsA.ClientId.ShouldBe("tenant-a-client-id");
            settingsA.ClientSecret.ShouldBe("tenant-a-secret");
        }

        using (_currentTenant.Change(tenantBId))
        {
            var settingsB = await _externalLoginSettingsManager.GetProviderSettingsAsync("GitHub");
            settingsB.Enabled.ShouldBeTrue();
            settingsB.ClientId.ShouldBe("tenant-b-client-id");
            settingsB.ClientSecret.ShouldBe("tenant-b-secret");
        }
    }

    [Fact]
    public async Task Unconfigured_tenant_falls_back_to_disabled()
    {
        var tenantId = await CreateTenantAsync($"ext-c-{Guid.NewGuid():N}".Substring(0, 20));

        using (_currentTenant.Change(tenantId))
        {
            var settings = await _externalLoginSettingsManager.GetProviderSettingsAsync("GitHub");
            settings.Enabled.ShouldBeFalse("未配置的租户走默认值（Enabled=false）");
        }
    }

    [Fact]
    public async Task Client_secret_is_stored_encrypted_and_read_as_plaintext()
    {
        var tenantId = await CreateTenantAsync($"ext-d-{Guid.NewGuid():N}".Substring(0, 20));
        const string plainSecret = "github-secret-plaintext-123";

        await _settingManager.SetForTenantAsync(
            tenantId, "AbpAdmin.Account.ExternalLogin.GitHub.ClientSecret", plainSecret);

        // 数据库里的原始值必须是密文
        var stored = await WithUnitOfWorkAsync(async () =>
            await _settingRepository.FindAsync(
                "AbpAdmin.Account.ExternalLogin.GitHub.ClientSecret", "T", tenantId.ToString()));

        stored.ShouldNotBeNull();
        stored!.Value.ShouldNotBeNullOrEmpty();
        stored.Value.ShouldNotBe(plainSecret, "ClientSecret 的设置值必须加密存储");

        // 读取侧（ISettingProvider）解密回明文
        using (_currentTenant.Change(tenantId))
        {
            var settings = await _externalLoginSettingsManager.GetProviderSettingsAsync("GitHub");
            settings.ClientSecret.ShouldBe(plainSecret);
        }
    }
}
