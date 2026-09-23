using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.TextTemplating;
using Xunit;

namespace AbpAdmin.TextTemplates;

/* T2.3 文本模板管理集成测试（03-batch2-pro-parity.md T2.3 验收标准：
 * 「StoredTemplateContentContributorTests.cs 覆盖：数据库覆盖生效、租户隔离、恢复默认、缓存失效」）。
 *
 * 核心断言是「数据库覆盖真实生效于模板渲染」：全部用例都走 ITemplateRenderer.RenderAsync
 * 真实渲染管线（StoredTemplateContentContributor 已 Insert(0) 进 AbpTextTemplatingOptions.ContentContributors），
 * 而不是直接调 contributor。
 *
 * 被测模板来自 TestTemplateDefinitionProvider（每个用例一个独立模板名，
 * 模板没有虚拟文件基线——恢复默认后内容应为 null）。
 *
 * 注意：渲染调用一律包在 WithUnitOfWorkAsync 里——contributor 内的仓储查询需要环境 UoW
 * （生产环境由请求 UoW 提供；没有环境 UoW 时 GetQueryableAsync 的拦截器 UoW 会在返回时
 * 释放 DbContext，随后的 FirstOrDefaultAsync 撞 ObjectDisposedException）。
 */
public abstract class StoredTemplateContentContributorTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITextTemplateAppService _templateAppService;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ITemplateContentProvider _templateContentProvider;
    private readonly ITemplateDefinitionManager _templateDefinitionManager;
    private readonly IRepository<TextTemplateContent, Guid> _contentRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;

    protected StoredTemplateContentContributorTests()
    {
        _templateAppService = GetRequiredService<ITextTemplateAppService>();
        _templateRenderer = GetRequiredService<ITemplateRenderer>();
        _templateContentProvider = GetRequiredService<ITemplateContentProvider>();
        _templateDefinitionManager = GetRequiredService<ITemplateDefinitionManager>();
        _contentRepository = GetRequiredService<IRepository<TextTemplateContent, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
    }

    /// <summary>
    /// 在环境 UoW 内渲染模板（模拟一次真实请求）。
    /// </summary>
    private Task<string> RenderAsync(string name, string? cultureName = null, object? model = null)
    {
        return WithUnitOfWorkAsync(async () =>
            await _templateRenderer.RenderAsync(name, model, cultureName));
    }

    private Task UpdateContentAsync(string name, string? cultureName, string content)
    {
        return _templateAppService.UpdateAsync(new UpdateTextTemplateDto
        {
            Name = name,
            CultureName = cultureName,
            Content = content
        });
    }

    private Task RestoreAsync(string name, string? cultureName = null)
    {
        return _templateAppService.RestoreToDefaultAsync(new RestoreTextTemplateToDefaultInput
        {
            Name = name,
            CultureName = cultureName
        });
    }

    [Fact]
    public async Task Database_Override_Should_Be_Used_By_TemplateRenderer()
    {
        // 核心验收点：界面上改的模板内容，系统里任何 ITemplateRenderer 调用都真实使用
        var name = TestTemplateDefinitionProvider.StoreOverride;
        try
        {
            await UpdateContentAsync(name, null, "数据库覆盖内容：{{ model.name }}");

            var rendered = await RenderAsync(name, model: new { name = "世界" });

            rendered.ShouldBe("数据库覆盖内容：世界");
        }
        finally
        {
            await RestoreAsync(name);
        }
    }

    [Fact]
    public async Task Culture_Fallback_Should_Be_Requested_Then_Parent_Then_Invariant()
    {
        var name = TestTemplateDefinitionProvider.CultureFallback;
        try
        {
            await UpdateContentAsync(name, null, "文化无关：{{ model.name }}");
            await UpdateContentAsync(name, "zh-Hans", "简体中文：{{ model.name }}");

            // 区域文化 zh-Hans-CN 精确未命中 → 回退父文化 zh-Hans
            (await RenderAsync(name, cultureName: "zh-Hans-CN", model: new { name = "甲" }))
                .ShouldBe("简体中文：甲");

            // 精确命中
            (await RenderAsync(name, cultureName: "zh-Hans", model: new { name = "乙" }))
                .ShouldBe("简体中文：乙");

            // 完全无关的文化 → 回退文化无关内容
            (await RenderAsync(name, cultureName: "fr", model: new { name = "丙" }))
                .ShouldBe("文化无关：丙");
        }
        finally
        {
            await RestoreAsync(name);
        }
    }

    [Fact]
    public async Task Tenant_Override_Should_Not_Affect_Host_And_Vice_Versa()
    {
        var name = TestTemplateDefinitionProvider.TenantIsolation;
        var tenant = await _tenantManager.CreateAsync($"tpl-test-{Guid.NewGuid():N}"[..20]);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });

        try
        {
            // host 覆盖
            await UpdateContentAsync(name, null, "HOST内容");
            (await RenderAsync(name)).ShouldBe("HOST内容");

            // 租户覆盖同一模板，只影响该租户（UoW 开在 CurrentTenant.Change 里面）
            using (_currentTenant.Change(tenant.Id))
            {
                await UpdateContentAsync(name, null, "TENANT内容");
                (await RenderAsync(name)).ShouldBe("TENANT内容");
            }

            // host 侧仍是 host 的内容
            (await RenderAsync(name)).ShouldBe("HOST内容");
        }
        finally
        {
            using (_currentTenant.Change(tenant.Id))
            {
                await RestoreAsync(name);
            }
            await RestoreAsync(name);
        }
    }

    [Fact]
    public async Task RestoreToDefault_Should_Delete_Record_And_Clear_Cache()
    {
        var name = TestTemplateDefinitionProvider.RestoreDefault;

        await UpdateContentAsync(name, null, "覆盖内容");

        // 先渲染一次，让 contributor 把内容写进缓存
        (await RenderAsync(name)).ShouldBe("覆盖内容");

        // Act - 恢复默认：删除当前租户上下文（host）的覆盖记录并清缓存
        await RestoreAsync(name);

        // Assert 1 - 数据库覆盖记录已删
        var remaining = await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _contentRepository.GetQueryableAsync();
            return await _contentRepository.AsyncExecuter.CountAsync(
                queryable.Where(x => x.TenantId == null && x.Name == name));
        });
        remaining.ShouldBe(0);

        // Assert 2 - 再次经过 contributor 管线取内容：缓存若没清会返回旧的「覆盖内容」，
        // 该模板没有虚拟文件基线，正确结果是 null
        await WithUnitOfWorkAsync(async () =>
        {
            var definition = await _templateDefinitionManager.GetAsync(name);
            (await _templateContentProvider.GetContentOrNullAsync(definition, cultureName: null))
                .ShouldBeNull();
        });
    }

    [Fact]
    public async Task Update_Should_Invalidate_Cache_So_Next_Render_Uses_New_Content()
    {
        var name = TestTemplateDefinitionProvider.CacheInvalidation;
        try
        {
            await UpdateContentAsync(name, null, "V1");
            // 第一次渲染把 V1 写进缓存
            (await RenderAsync(name)).ShouldBe("V1");

            // Act - 改内容，不重启
            await UpdateContentAsync(name, null, "V2");

            // Assert - 下一次渲染即用新内容（缓存失效链路正确）
            (await RenderAsync(name)).ShouldBe("V2");
        }
        finally
        {
            await RestoreAsync(name);
        }
    }

    [Fact]
    public async Task Update_Should_Invalidate_Cache_For_Cultures_Outside_Common_List()
    {
        // 曾经的负债：缓存失效按硬编码文化清单逐条 Remove，清单外文化（如 fr）保存后
        // 要等 1 小时滑动过期才不脏读。版本号化后 (Tenant, Name) 维度一次作废所有文化，
        // 本用例在旧实现下必然失败——守住修复。
        var name = TestTemplateDefinitionProvider.CacheInvalidation;
        try
        {
            await UpdateContentAsync(name, null, "COMMON-V1");
            (await RenderAsync(name, cultureName: "fr")).ShouldBe("COMMON-V1");

            await UpdateContentAsync(name, null, "COMMON-V2");

            (await RenderAsync(name, cultureName: "fr")).ShouldBe("COMMON-V2");
        }
        finally
        {
            await RestoreAsync(name);
        }
    }
}
