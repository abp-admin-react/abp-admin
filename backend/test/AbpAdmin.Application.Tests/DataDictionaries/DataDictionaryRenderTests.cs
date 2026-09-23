using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Identity;
using EasyAbp.Abp.DataDictionary;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.DataDictionaries;

/* T3.4 第 3 / 9 步：规则扫描与后端渲染。
 * 覆盖验收标准：「ScanRules 执行过且扫到了规则（数量必须大于 0）」
 * 与「导出路径上字典字段输出显示文本而不是编码」。 */
public abstract class DataDictionaryRenderTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOptions<AbpDataDictionaryOptions> _options;
    private readonly IDataDictionaryRenderer _renderer;

    protected DataDictionaryRenderTests()
    {
        _options = GetRequiredService<IOptions<AbpDataDictionaryOptions>>();
        _renderer = GetRequiredService<IDataDictionaryRenderer>();
    }

    [Fact]
    public void Should_Have_Registered_Render_Rules_On_Startup()
    {
        // AbpAdminApplicationModule.OnApplicationInitialization 手动扫描两个程序集后注册。
        // 数量为 0 意味着扫错了程序集——那是静默失效，这条测试就是防它的。
        _options.Value.Rules.Count.ShouldBeGreaterThan(0);
        _options.Value.Rules.ShouldContain(r => r.DtoType == typeof(UserExportRenderDto));
    }

    [Fact]
    public async Task Renderer_Should_Render_DisplayText_For_Export_Dto()
    {
        // 导出路径的渲染：编码（Enabled）→ 字典显示文本（启用）。
        // 渲染器的值提供器未命中缓存时会查仓储，需要环境 UoW。
        var rendered = await WithUnitOfWorkAsync(async () =>
            await _renderer.RenderAsync(new UserExportRenderDto
            {
                IsActiveCode = AbpAdminDictionaryCodes.EnabledStatusItems.Enabled
            }));

        rendered.IsActiveText.ShouldBe("启用");
    }
}
