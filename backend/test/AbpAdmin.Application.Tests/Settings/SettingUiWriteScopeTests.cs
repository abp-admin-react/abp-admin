using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.Abp.SettingUi;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.Settings;

/// <summary>
/// SettingUi 写入面作用域测试（安全审计 run-2 C-3 回归锁）：
/// SetSettingValuesAsync 只处理 UI 注册设置（WithUi 挂 Group1 的定义）——
/// 与 ResetSettingValuesAsync 同口径。非 UI 的域设置（资金开关形态：G+D providers、
/// 故意 omit WithUi、唯一写门是域权限端点）即便以表单键/裸名提交也必须零层写入；
/// UI 注册设置正常生效（对照组）。
/// 测试专用定义由 <see cref="SettingUiWriteScopeTestSettingProvider"/> 提供，
/// 经 EfCore 侧启动模块注册（AbpSettingOptions.DefinitionProviders）。
/// </summary>
public abstract class SettingUiWriteScopeTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISettingUiAppService _settingUiAppService;
    private readonly ISettingProvider _settingProvider;

    protected SettingUiWriteScopeTests()
    {
        _settingUiAppService = GetRequiredService<ISettingUiAppService>();
        _settingProvider = GetRequiredService<ISettingProvider>();
    }

    [Fact]
    public async Task Non_Ui_Domain_Setting_Is_Skipped_By_The_Write_Face()
    {
        // 域设置以表单键提交（上游按名盲解析任意定义时的利用形态）
        await _settingUiAppService.SetSettingValuesAsync(new Dictionary<string, string>
        {
            [FormKey(SettingUiWriteScopeTestSettingProvider.DomainOnlyName)] = "attacker-value",
        });

        (await _settingProvider.GetOrNullAsync(SettingUiWriteScopeTestSettingProvider.DomainOnlyName))
            .ShouldBe("domain-default"); // 零层写入，读回落定义默认值
    }

    [Fact]
    public async Task Non_Ui_Domain_Setting_Is_Skipped_Even_When_Submitted_As_Bare_Name()
    {
        // 裸名经 NormalizeToFormKeys 规范化后同样必须被过滤（规范化拓宽了入口，不能拓宽作用域）
        await _settingUiAppService.SetSettingValuesAsync(new Dictionary<string, string>
        {
            [SettingUiWriteScopeTestSettingProvider.DomainOnlyName] = "attacker-value",
        });

        (await _settingProvider.GetOrNullAsync(SettingUiWriteScopeTestSettingProvider.DomainOnlyName))
            .ShouldBe("domain-default");
    }

    [Fact]
    public async Task Ui_Registered_Setting_Is_Still_Writable()
    {
        await _settingUiAppService.SetSettingValuesAsync(new Dictionary<string, string>
        {
            [FormKey(SettingUiWriteScopeTestSettingProvider.UiRegisteredName)] = "ui-new-value",
        });

        (await _settingProvider.GetOrNullAsync(SettingUiWriteScopeTestSettingProvider.UiRegisteredName))
            .ShouldBe("ui-new-value");
    }

    /// <summary>设置名 → 上游表单键形态（Setting_A_B_C）。测试用定义的段本身是 PascalCase，
    /// 与 NormalizeToFormKeys 的分段 PascalCase 变换在此名字上互为恒等，直拼即得。</summary>
    private static string FormKey(string settingName) =>
        SettingUiConst.FormNamePrefix + string.Join("_", settingName.Split('.'));
}
