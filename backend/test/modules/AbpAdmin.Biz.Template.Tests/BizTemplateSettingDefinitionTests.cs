using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.Biz.Template.Settings;

/// <summary>
/// 第③层（运行期参数）定义契约测试：
/// 参数名 / 默认值 / 设置页 UI 元数据（Group1=模块自带 Tab、Type=number）三类字面量
/// 在 Provider、本地化 JSON、前端映射三处必须同源——这里钉住后端那一端，
/// 任何一端改字面量（如组名、控件类型）本组用例先红。
/// </summary>
public class BizTemplateSettingDefinitionTests : BizTemplateTestBase
{
    private readonly ISettingDefinitionManager _definitionManager;
    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;

    public BizTemplateSettingDefinitionTests()
    {
        _definitionManager = GetRequiredService<ISettingDefinitionManager>();
        _settingProvider = GetRequiredService<ISettingProvider>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    [Fact]
    public async Task Definition_Should_Have_Default_And_Ui_Metadata()
    {
        var definition = await _definitionManager.GetAsync(BizTemplateSettings.Project.MaxProjectsPerUser);

        definition.DefaultValue.ShouldBe(BizTemplateSettings.Project.DefaultMaxProjectsPerUser.ToString());
        definition.Properties[SettingUiConst.Group1].ShouldBe(BizTemplateSettings.SettingUiGroup);
        definition.Properties[SettingUiConst.Type].ShouldBe(SettingUiConst.Components.Number);
    }

    [Fact]
    public async Task Setting_Default_Should_Resolve_Without_Stored_Value()
    {
        // 无存储值时 SettingProvider 回落定义默认（字符串口径）——配额 fail-closed 兜底的取值来源
        (await _settingProvider.GetOrNullAsync(BizTemplateSettings.Project.MaxProjectsPerUser))
            .ShouldBe(BizTemplateSettings.Project.DefaultMaxProjectsPerUser.ToString());
    }

    [Fact]
    public async Task Stored_Value_Should_Override_Default_And_Read_Back()
    {
        await _settingManager.SetGlobalAsync(BizTemplateSettings.Project.MaxProjectsPerUser, "5");

        (await _settingProvider.GetOrNullAsync(BizTemplateSettings.Project.MaxProjectsPerUser))
            .ShouldBe("5");
    }

    [Fact]
    public void Module_Localization_Keys_Should_Resolve()
    {
        // 本地化链路契约：模块资源（嵌入 JSON → BizTemplateResource）必须能解析
        // 设置项显示名/描述与业务异常文案；解析失败时 ABP 返回裸键，设置页与配额报错会露英文键名。
        var localizer = GetRequiredService<IStringLocalizer<BizTemplateResource>>();

        localizer["BizProject:QuotaExceeded"].Value.ShouldNotBe("BizProject:QuotaExceeded");
        localizer["DisplayName:BizTemplate.Project.MaxProjectsPerUser"].Value
            .ShouldNotBe("DisplayName:BizTemplate.Project.MaxProjectsPerUser");
        localizer["Description:BizTemplate.Project.MaxProjectsPerUser"].Value
            .ShouldNotBe("Description:BizTemplate.Project.MaxProjectsPerUser");
        localizer["BizTemplate"].Value.ShouldNotBe("BizTemplate");
    }
}
