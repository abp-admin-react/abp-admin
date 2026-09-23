using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Localization;

/* T2.2 语言管理集成测试（03-batch2-pro-parity.md T2.2 验收标准：
 * 「LanguageAppServiceTests.cs 与 LanguageTextAppServiceTests.cs 覆盖业务规则与租户隔离」）。
 *
 * 覆盖：种子语言（20 条、zh-Hans 默认）、创建、重复 CultureName 拒绝、
 * Update 不改 CultureName、删除/禁用默认语言拒绝、SetAsDefault 同步设置项与 IsDefault、
 * 租户上下文可见 host 语言列表（Language 无 TenantId）。
 *
 * 测试环境种子：LanguageDataSeedContributor 在 host 上下文跑过，AppLanguages 已有 20 条。
 */
public abstract class LanguageAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ILanguageAppService _languageAppService;
    private readonly ISettingProvider _settingProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;

    protected LanguageAppServiceTests()
    {
        _languageAppService = GetRequiredService<ILanguageAppService>();
        _settingProvider = GetRequiredService<ISettingProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
    }

    [Fact]
    public async Task GetListAsync_Should_Return_Seeded_Languages_With_ZhHans_Default()
    {
        var result = await _languageAppService.GetListAsync();

        // 空库种子后 20 条（AbpLocalizationOptions 静态清单），zh-Hans 为默认
        result.Items.Count.ShouldBeGreaterThanOrEqualTo(20);
        result.Items.ShouldContain(x => x.CultureName == "en");
        var zhHans = result.Items.FirstOrDefault(x => x.CultureName == "zh-Hans");
        zhHans.ShouldNotBeNull();
        zhHans.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_Should_Add_New_Language()
    {
        var created = await _languageAppService.CreateAsync(new CreateLanguageDto
        {
            CultureName = "de-CH",
            UiCultureName = "de-CH",
            DisplayName = "German (Switzerland)",
            IsEnabled = true
        });

        try
        {
            created.Id.ShouldNotBe(Guid.Empty);
            created.IsDefault.ShouldBeFalse();

            var list = await _languageAppService.GetListAsync();
            list.Items.ShouldContain(x => x.CultureName == "de-CH");
        }
        finally
        {
            await _languageAppService.DeleteAsync(created.Id);
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Duplicate_CultureName()
    {
        // 种子里已有 en，重复创建必须抛业务异常而不是 500
        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            _languageAppService.CreateAsync(new CreateLanguageDto
            {
                CultureName = "en",
                UiCultureName = "en",
                DisplayName = "English duplicate"
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Localization.LanguageAlreadyExists);
    }

    [Fact]
    public async Task UpdateAsync_Should_Not_Change_Culture_Name()
    {
        // UpdateLanguageDto 不含 CultureName/UiCultureName 字段，更新后两者必须保持不变
        var list = await _languageAppService.GetListAsync();
        var target = list.Items.First(x => x.CultureName == "fr");

        var updated = await _languageAppService.UpdateAsync(target.Id, new UpdateLanguageDto
        {
            DisplayName = "French (updated)",
            IsEnabled = true
        });

        updated.DisplayName.ShouldBe("French (updated)");
        updated.CultureName.ShouldBe("fr");
        updated.UiCultureName.ShouldBe("fr");

        // 恢复显示名，避免污染其他用例
        await _languageAppService.UpdateAsync(target.Id, new UpdateLanguageDto
        {
            DisplayName = target.DisplayName,
            IsEnabled = target.IsEnabled
        });
    }

    [Fact]
    public async Task DeleteAsync_Should_Reject_Default_Language()
    {
        var list = await _languageAppService.GetListAsync();
        var defaultLanguage = list.Items.First(x => x.IsDefault);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            _languageAppService.DeleteAsync(defaultLanguage.Id));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Localization.CannotDeleteDefaultLanguage);
    }

    [Fact]
    public async Task UpdateAsync_Should_Reject_Disabling_Default_Language()
    {
        var list = await _languageAppService.GetListAsync();
        var defaultLanguage = list.Items.First(x => x.IsDefault);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            _languageAppService.UpdateAsync(defaultLanguage.Id, new UpdateLanguageDto
            {
                DisplayName = defaultLanguage.DisplayName,
                IsEnabled = false
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Localization.CannotDisableDefaultLanguage);
    }

    [Fact]
    public async Task SetAsDefaultAsync_Should_Update_Flags_And_Setting()
    {
        var list = await _languageAppService.GetListAsync();
        var zhHans = list.Items.First(x => x.CultureName == "zh-Hans");
        var en = list.Items.First(x => x.CultureName == "en");
        zhHans.IsDefault.ShouldBeTrue();

        try
        {
            // Act
            await _languageAppService.SetAsDefaultAsync(en.Id);

            // Assert - IsDefault 标记互换
            var after = await _languageAppService.GetListAsync();
            after.Items.First(x => x.CultureName == "en").IsDefault.ShouldBeTrue();
            after.Items.First(x => x.CultureName == "zh-Hans").IsDefault.ShouldBeFalse();

            // Assert - 框架设置项同步（LocalizationSettingNames.DefaultLanguage）
            (await _settingProvider.GetOrNullAsync(LocalizationSettingNames.DefaultLanguage))
                .ShouldBe("en");
        }
        finally
        {
            // 恢复 zh-Hans 为默认，避免影响其他用例
            await _languageAppService.SetAsDefaultAsync(zhHans.Id);
        }
    }

    [Fact]
    public async Task SetAsDefaultAsync_Should_Reject_Disabled_Language()
    {
        var created = await _languageAppService.CreateAsync(new CreateLanguageDto
        {
            CultureName = "de-CH",
            UiCultureName = "de-CH",
            DisplayName = "German (Switzerland)",
            IsEnabled = false
        });

        try
        {
            var exception = await Assert.ThrowsAsync<BusinessException>(() =>
                _languageAppService.SetAsDefaultAsync(created.Id));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Localization.CannotSetDisabledLanguageAsDefault);
        }
        finally
        {
            await _languageAppService.DeleteAsync(created.Id);
        }
    }

    [Fact]
    public async Task GetListAsync_Should_Return_Host_Languages_In_Tenant_Context()
    {
        // Arrange - Language 无 TenantId，租户上下文不被多租户筛选器挡掉
        var tenant = await _tenantManager.CreateAsync($"lang-test-{Guid.NewGuid():N}"[..20]);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });

        using (_currentTenant.Change(tenant.Id))
        {
            // Act
            var result = await _languageAppService.GetListAsync();

            // Assert
            result.Items.Count.ShouldBeGreaterThanOrEqualTo(20);
            result.Items.ShouldContain(x => x.CultureName == "zh-Hans");
        }
    }
}
