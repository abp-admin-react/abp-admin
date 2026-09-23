using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Validation;
using Xunit;

namespace AbpAdmin.Localization;

/* T2.2 语言管理集成测试：本地化文本覆盖（03-batch2-pro-parity.md T2.2 验收标准：
 * 覆盖保存后不重启即生效、恢复默认回到静态 json 原文、租户覆盖不外泄）。
 *
 * 「生效」断言走真实本地化管线：IStringLocalizer<AbpAdminResource> 的 contributor 链里
 * 有 DbLocalizationResourceContributor（Domain 模块初始化时给所有资源挂上），
 * 它经 DbExternalLocalizationStore 读 AppLanguageTexts 表。
 *
 * 测试键："Home"（静态 json zh-Hans = "首页"）。
 */
public abstract class LanguageTextAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ResourceName = "AbpAdmin";
    private const string CultureName = "zh-Hans";
    private const string TestKey = "Home";
    private const string StaticValue = "首页";

    private readonly ILanguageTextAppService _languageTextAppService;
    private readonly ILanguageTextRepository _languageTextRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly StaticLocalizationTextProvider _staticTextProvider;

    protected LanguageTextAppServiceTests()
    {
        _languageTextAppService = GetRequiredService<ILanguageTextAppService>();
        _languageTextRepository = GetRequiredService<ILanguageTextRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _staticTextProvider = GetRequiredService<StaticLocalizationTextProvider>();
    }

    /// <summary>
    /// 用全新 scope 解析 localizer 读取文本，模拟「另一次请求」（验证无需重启）。
    /// </summary>
    private string ReadLocalizedValue()
    {
        using var scope = ServiceProvider.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<AbpAdminResource>>();
        return localizer[TestKey].Value;
    }

    private async Task CleanupOverrideAsync(Guid? tenantId)
    {
        using (_currentTenant.Change(tenantId))
        {
            // 直接恢复默认（存在则删），并清缓存
            await _languageTextAppService.RestoreToDefaultAsync(ResourceName, CultureName, TestKey);
        }
    }

    [Fact]
    public async Task UpdateAsync_Should_Take_Effect_Through_Localization_Pipeline_Without_Restart()
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(CultureName);
        try
        {
            // 基线：静态 json 原文
            ReadLocalizedValue().ShouldBe(StaticValue);

            // Act 1 - 保存覆盖
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "覆盖后的首页"
            });

            // Assert 1 - 不重启，立刻读到覆盖值（缓存清除链路正确）
            ReadLocalizedValue().ShouldBe("覆盖后的首页");

            // Act 2 - 恢复默认
            await _languageTextAppService.RestoreToDefaultAsync(ResourceName, CultureName, TestKey);

            // Assert 2 - 回到静态 json 原文，且库里覆盖记录已删（直接查库断言）
            ReadLocalizedValue().ShouldBe(StaticValue);
            (await _languageTextRepository.FindAsync(null, ResourceName, CultureName, TestKey)).ShouldBeNull();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task UpdateAsync_Should_Upsert_Existing_Override()
    {
        try
        {
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "第一版"
            });

            // 覆盖行返回口径（ToOverrideRowDto 的契约）：恒 IsOverridden=true、BaseValue=null、有 Id。
            // 这也是 Mapperly 收拢为手写映射的回归守卫——漏映射事故形态正是静默产出 IsOverridden=false
            var updated = await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "第二版"
            });
            updated.Value.ShouldBe("第二版");
            updated.IsOverridden.ShouldBeTrue();
            updated.BaseValue.ShouldBeNull();
            updated.Id.ShouldNotBeNull();

            // 同一键只保留一条记录，值为最新（直接查库验证）
            var text = await _languageTextRepository.FindAsync(null, ResourceName, CultureName, TestKey);
            text.ShouldNotBeNull();
            text!.Value.ShouldBe("第二版");

            var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Filter = TestKey
            });
            list.Items.Count(x => x.Name == TestKey).ShouldBe(1);
        }
        finally
        {
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task Tenant_Override_Should_Not_Leak_To_Host()
    {
        var tenant = await _tenantManager.CreateAsync($"lang-text-{Guid.NewGuid():N}"[..20]);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });

        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(CultureName);
        try
        {
            // host 覆盖
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "HOST覆盖值"
            });
            ReadLocalizedValue().ShouldBe("HOST覆盖值");

            using (_currentTenant.Change(tenant.Id))
            {
                // 租户未覆盖时回退到 host 覆盖值
                ReadLocalizedValue().ShouldBe("HOST覆盖值");

                // 租户覆盖同一个 key
                await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
                {
                    ResourceName = ResourceName,
                    CultureName = CultureName,
                    Name = TestKey,
                    Value = "TENANT覆盖值"
                });
                ReadLocalizedValue().ShouldBe("TENANT覆盖值");
            }

            // 租户覆盖不外泄：host 侧仍是 host 的值
            ReadLocalizedValue().ShouldBe("HOST覆盖值");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            await CleanupOverrideAsync(tenant.Id);
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetResourceNamesAsync_Should_Return_Registered_Resources()
    {
        var result = await _languageTextAppService.GetResourceNamesAsync();

        result.Items.ShouldContain(ResourceName);
    }

    /* 以下为"静态基线合并视图"用例（对标 Pro 静态文本外部存储）：
     * 只查覆盖表看不到从未覆盖的 key——列表必须把静态 json 里的 key 一并纳入。 */

    [Fact]
    public async Task GetListAsync_Should_Include_Never_Overridden_Static_Keys()
    {
        try
        {
            // 无任何覆盖行："Home" 只存在于静态 json
            var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Filter = TestKey
            });

            var row = list.Items.Single(x => x.Name == TestKey);
            row.Value.ShouldBe(StaticValue); // 生效值 = 静态原文（与本地化管线口径一致）
            row.IsOverridden.ShouldBeFalse();
            row.Id.ShouldBeNull();
        }
        finally
        {
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetListAsync_Should_Provide_Base_Culture_Comparison()
    {
        // "Menu:Home" 在 zh-Hans 与 en 的静态 json 里都有（"首页" / "Home"）
        const string baseKey = "Menu:Home";
        try
        {
            var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                BaseCultureName = "en",
                Filter = baseKey
            });

            var row = list.Items.Single(x => x.Name == baseKey);
            row.BaseValue.ShouldBe("Home");
            row.Value.ShouldBe("首页"); // 目标文化静态基线
        }
        finally
        {
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetListAsync_OnlyEmpty_Should_Find_Explicit_Untranslated_Overrides()
    {
        try
        {
            // 显式空串覆盖 = 未翻译合法态；合并视图下 OnlyEmpty 能找到它
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = ""
            });

            var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Filter = TestKey,
                OnlyEmpty = true
            });

            var row = list.Items.Single(x => x.Name == TestKey);
            row.Value.ShouldBe("");
            row.IsOverridden.ShouldBeTrue();
        }
        finally
        {
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetListAsync_IsOverridden_Should_Follow_Current_Context_Not_Merged_View()
    {
        // IsOverridden 与「恢复默认」的实际效果必须同口径（Pro 的 per-context 语义）：
        // host 覆盖对租户生效（Value = host 值），但租户没有自己的覆盖行 → IsOverridden = false。
        // 此时恢复默认只会删租户层（没有可删的行），值不会变化——按钮必须不可点。
        var tenant = await _tenantManager.CreateAsync($"lang-text-{Guid.NewGuid():N}"[..20]);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });

        try
        {
            // host 建覆盖
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "HOST覆盖值"
            });

            // host 自己的列表：本层有覆盖 → 生效值=host 值，可恢复
            var hostList = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Filter = TestKey
            });
            var hostRow = hostList.Items.Single(x => x.Name == TestKey);
            hostRow.Value.ShouldBe("HOST覆盖值");
            hostRow.IsOverridden.ShouldBeTrue();

            using (_currentTenant.Change(tenant.Id))
            {
                // 租户列表：生效值是 host 覆盖（管线口径），但本层无覆盖行 → 不可恢复
                var tenantList = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
                {
                    ResourceName = ResourceName,
                    CultureName = CultureName,
                    Filter = TestKey
                });
                var tenantRow = tenantList.Items.Single(x => x.Name == TestKey);
                tenantRow.Value.ShouldBe("HOST覆盖值");
                tenantRow.IsOverridden.ShouldBeFalse();

                // 租户自己覆盖后：生效值切到租户覆盖，本层有覆盖行 → 可恢复
                await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
                {
                    ResourceName = ResourceName,
                    CultureName = CultureName,
                    Name = TestKey,
                    Value = "TENANT覆盖值"
                });

                var tenantListAfter = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
                {
                    ResourceName = ResourceName,
                    CultureName = CultureName,
                    Filter = TestKey
                });
                var tenantRowAfter = tenantListAfter.Items.Single(x => x.Name == TestKey);
                tenantRowAfter.Value.ShouldBe("TENANT覆盖值");
                tenantRowAfter.IsOverridden.ShouldBeTrue();

                // 租户层恢复：只删租户覆盖行 → 生效值回落到 host 覆盖（不是静态原文），
                // host 侧的行原样保留——per-context 恢复语义的另一半
                await _languageTextAppService.RestoreToDefaultAsync(ResourceName, CultureName, TestKey);

                var tenantRowRestored = (await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
                {
                    ResourceName = ResourceName,
                    CultureName = CultureName,
                    Filter = TestKey
                })).Items.Single(x => x.Name == TestKey);
                tenantRowRestored.Value.ShouldBe("HOST覆盖值");
                tenantRowRestored.IsOverridden.ShouldBeFalse();
            }

            // host 侧不受租户恢复影响
            var hostRowAfter = (await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Filter = TestKey
            })).Items.Single(x => x.Name == TestKey);
            hostRowAfter.Value.ShouldBe("HOST覆盖值");
            hostRowAfter.IsOverridden.ShouldBeTrue();
        }
        finally
        {
            await CleanupOverrideAsync(tenant.Id);
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetListAsync_Should_Merge_Across_Resources_When_Resource_Omitted()
    {
        // 资源可选（Pro 同款）：不传 ResourceName = 跨全部注册资源的静态并集 + 覆盖行，
        // 覆盖行照常带生效值与 IsOverridden，其他资源的静态 key 也进列表（漏译可见）
        try
        {
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "override-v1"
            });

            // 不传 ResourceName，只传目标文化
            var allResources = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                CultureName = CultureName,
                Filter = TestKey
            });

            // 覆盖行在跨资源列表里口径不变
            var overrideRow = allResources.Items.Single(x =>
                x.ResourceName == ResourceName && x.Name == TestKey);
            overrideRow.Value.ShouldBe("override-v1");
            overrideRow.IsOverridden.ShouldBeTrue();

            // 跨资源：列表里有其他资源的静态 key（ABP 框架资源在测试宿主同样注册）
            allResources.Items.ShouldContain(x => x.ResourceName != ResourceName);

            // 对照：指定资源时只有本资源的行（总数严格更小，证明资源过滤仍然生效）
            var singleResource = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Filter = TestKey
            });
            singleResource.TotalCount.ShouldBeLessThan(allResources.TotalCount);
        }
        finally
        {
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetListAsync_Should_Reject_Missing_CultureName()
    {
        // 目标文化改为必选（页面默认选中启用语言，空值只可能来自裸 API 调用）：
        // 静态基线与覆盖的取值/回退都依赖目标文化，与其猜默认不如明确拒绝
        await Should.ThrowAsync<AbpValidationException>(() =>
            _languageTextAppService.GetListAsync(new GetLanguageTextsInput
            {
                ResourceName = ResourceName
                // CultureName 缺失 → ABP 输入校验 400
            }));
    }

    [Fact]
    public async Task GetListAsync_Should_Page_The_Merged_Universe_Consistently()
    {
        // 分页已从 DB 下推改为内存合并：钉住 Skip/Take 与全量有序列表的切片一致性
        // （第 2 页起不重不漏，防止"对过滤前分页"或排序退化类回归）
        var full = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = CultureName,
            MaxResultCount = 1000
        });
        full.TotalCount.ShouldBeGreaterThan(2);

        var page = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = CultureName,
            SkipCount = 1,
            MaxResultCount = 2
        });

        page.TotalCount.ShouldBe(full.TotalCount);
        page.Items.Select(x => (x.Name, x.Value)).ShouldBe(
            full.Items.Skip(1).Take(2).Select(x => (x.Name, x.Value)).ToList());
    }

    [Fact]
    public async Task GetListAsync_Filter_Should_Match_Value_Not_Only_Name()
    {
        // 过滤口径：key 或生效值模糊匹配、大小写不敏感——用中文值命中英文 key 是最直接的回归
        var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = CultureName,
            Filter = StaticValue // "首页"，key 是 "Home"
        });

        list.Items.ShouldContain(x => x.Name == TestKey && x.Value == StaticValue);
    }

    [Fact]
    public async Task StaticLocalizationTextProvider_Should_Fall_Back_Along_Culture_Chain()
    {
        // 文化回退链：en-GB → en。两个断言各钉一半：
        // ① 并集——"Permission:AbpAdmin" 仅存在于 en.json，en-GB 请求能取到（粗层 key 并入）
        // ② 方向——"Texts:CultureFallbackProbe" 在 en/en-GB 取值不同，en-GB（越具体层）必须胜出；
        //    若合并方向反了（粗文化盖掉具体文化），本用例会红
        var texts = await _staticTextProvider.GetTextsAsync(ResourceName, "en-GB");
        texts["Permission:AbpAdmin"].ShouldBe("AbpAdmin");
        texts["Texts:CultureFallbackProbe"].ShouldBe("Specific culture value (en-GB)");
    }

    [Fact]
    public async Task StaticLocalizationTextProvider_Should_Return_Empty_For_Invalid_Culture()
    {
        // 非法文化名：CultureNotFoundException 被 fail-closed 成空字典（列表只剩覆盖行，不抛 500）
        var texts = await _staticTextProvider.GetTextsAsync(ResourceName, "not-a-culture");
        texts.ShouldBeEmpty();
    }

    [Fact]
    public async Task StaticLocalizationTextProvider_Should_Return_Empty_For_Unknown_Resource()
    {
        // 未注册资源：同款 fail-closed 空基线
        var texts = await _staticTextProvider.GetTextsAsync("No-Such-Resource", CultureName);
        texts.ShouldBeEmpty();
    }

    [Fact]
    public async Task StaticLocalizationTextProvider_Should_Exclude_Db_Overrides()
    {
        // 「静态基线」的定义性语义：includeDynamicContributors=false 排除 DB 覆盖（IsDynamic 贡献者）。
        // 覆盖值一旦渗入静态基线，两路值一致、泄漏不可观测——必须有专门的守卫用例
        try
        {
            await _languageTextAppService.UpdateAsync(new UpdateLanguageTextDto
            {
                ResourceName = ResourceName,
                CultureName = CultureName,
                Name = TestKey,
                Value = "覆盖值"
            });

            var texts = await _staticTextProvider.GetTextsAsync(ResourceName, CultureName);
            texts[TestKey].ShouldBe(StaticValue); // 静态原文，不是覆盖值
        }
        finally
        {
            await CleanupOverrideAsync(null);
        }
    }

    [Fact]
    public async Task GetListAsync_Filter_Should_Be_Case_Insensitive()
    {
        // Filter 大小写不敏感（OrdinalIgnoreCase）：小写 "home" 必须命中 key "Home"；
        // 回归成大小写敏感（如误用 Ordinal）时本用例变红
        var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = CultureName,
            Filter = TestKey.ToLowerInvariant()
        });

        list.Items.ShouldContain(x => x.Name == TestKey);
    }

    [Fact]
    public async Task GetListAsync_BaseValue_Should_Fall_Back_To_Resource_Default_Culture()
    {
        // Pro 同款（官方文档：基准文化列包含资源默认文化与父文化的回退值，目标列不回退）：
        // 探针 key 在目标 zh-Hans 与默认文化 en 都有值、基准 zh-Hant 链上没有——
        // BaseValue 必须取默认文化值；目标 Value 仍是 zh-Hans 原文（目标列不并入默认文化）
        var list = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = "zh-Hans",
            BaseCultureName = "zh-Hant",
            Filter = "BaseDefaultCultureProbe"
        });

        var row = list.Items.Single(x => x.Name == "Texts:BaseDefaultCultureProbe");
        row.Value.ShouldBe("zh-Hans 原文");
        row.BaseValue.ShouldBe("Default culture value (en)");
    }

    [Fact]
    public async Task GetListAsync_Should_Honor_Whitelisted_Sorting()
    {
        // Sorting 白名单（官方 PagedAndSorted 契约的内存视图实现）：value desc 全列非升序
        var byValueDesc = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = CultureName,
            Sorting = "value desc",
            MaxResultCount = 1000
        });
        var values = byValueDesc.Items.Select(x => x.Value).ToList();
        for (var i = 1; i < values.Count; i++)
        {
            (string.CompareOrdinal(values[i - 1], values[i]) >= 0).ShouldBeTrue();
        }

        // 未知字段静默忽略并回退默认序：单资源下等价于 Name 升序（分页切片稳定的前提）
        var unknownField = await _languageTextAppService.GetListAsync(new GetLanguageTextsInput
        {
            ResourceName = ResourceName,
            CultureName = CultureName,
            Sorting = "notASortField desc",
            MaxResultCount = 1000
        });
        var names = unknownField.Items.Select(x => x.Name).ToList();
        for (var i = 1; i < names.Count; i++)
        {
            (string.CompareOrdinal(names[i - 1], names[i]) <= 0).ShouldBeTrue();
        }
    }
}
