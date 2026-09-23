using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EasyAbp.Abp.SettingUi;
using Microsoft.Extensions.FileProviders;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.Settings;
using Volo.Abp.VirtualFileSystem;
using Xunit;

namespace AbpAdmin.Settings;

/// <summary>
/// 钉住"上游 SettingUiAppService.SetSettingValuesAsync 的 dateTime 分支 bug 对我们无效"这一前提。
/// 上游 bug（2026-09 main 分支核实，2.10.0 同）：dateTime 类型的设置项在
/// "无时区标记 + 用户设了时区 + 解析成功"路径上误写 return（应为 continue），
/// 不但该项自身不保存，还会中断循环、静默丢弃同一请求里后续所有设置项。
/// 我们没有覆写整个方法（复制 100 行换不来当下收益），前提是系统里没有任何
/// dateTime 类型的设置项——本测试钉住这个前提：一旦有人新增 dateTime 项，
/// 这里先红，提醒必须整体覆写 SetSettingValuesAsync 并顺手修掉上游 bug
/// （AbpAdminSettingUiAppService.SetSettingValuesAsync 注释有交接说明）。
/// （前端 SettingGroupPanel 的 DateTime 控件同理受影响，一并不建议使用。）
/// </summary>
public abstract class SettingUiDateTimePinTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly IVirtualFileProvider _virtualFileProvider;

    protected SettingUiDateTimePinTests()
    {
        _settingDefinitionManager = GetRequiredService<ISettingDefinitionManager>();
        _virtualFileProvider = GetRequiredService<IVirtualFileProvider>();
    }

    [Fact]
    public async Task No_Setting_Should_Declare_DateTime_Type_In_Definition_Properties()
    {
        var definitions = await _settingDefinitionManager.GetAllAsync();

        // 上游判定用的键是 SettingUiConst.Type（"Type"，Pascal），
        // 值大小写不敏感（上游 Equals(..., OrdinalIgnoreCase)），这里同口径。
        var offenders = definitions
            .Where(d => d.Properties.TryGetValue(SettingUiConst.Type, out var type)
                        && type?.ToString()?.Equals(SettingUiConst.Components.DateTime, System.StringComparison.OrdinalIgnoreCase) == true)
            .Select(d => d.Name)
            .ToList();

        offenders.ShouldBeEmpty(
            "以下设置项声明了 dateTime 类型，会踩中上游 SetSettingValuesAsync 的 return-中断 bug" +
            "（详见类注释）。要么换类型，要么整体覆写 SetSettingValuesAsync 修复后再用：\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void No_Setting_Properties_File_Should_Declare_DateTime_Type()
    {
        // SettingUi 的属性元数据有两个来源：SettingDefinition.Properties（上面那条）
        // 和虚拟文件系统 /SettingProperties 目录下的 *.json（本条，键为 Pascal 的 "Type"）。
        // 两处都不允许出现 dateTime。
        var offenders = _virtualFileProvider
            .GetDirectoryContents(SettingUiConst.SettingPropertiesFileFolder)
            .Where(f => f.Name.EndsWith(".json"))
            .SelectMany(f => ParseTypeProperties(f)
                .Where(kv => kv.Value.Equals(SettingUiConst.Components.DateTime, System.StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"{f.Name}:{kv.Key}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "以下 SettingProperties 文件里的设置项声明了 dateTime 类型（上游 return-中断 bug，见类注释）：\n" +
            string.Join("\n", offenders));
    }

    private static Dictionary<string, string> ParseTypeProperties(IFileInfo file)
    {
        // 与上游 GetMergedSettingPropertiesAsync 同构：
        // { "Setting.Name": { "Group1": "...", "Type": "text", ... } }
        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        using var json = JsonDocument.Parse(reader.ReadToEnd());

        var result = new Dictionary<string, string>();
        foreach (var setting in json.RootElement.EnumerateObject())
        {
            if (setting.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var prop in setting.Value.EnumerateObject())
            {
                if (prop.Name == SettingUiConst.Type && prop.Value.ValueKind == JsonValueKind.String)
                {
                    result[setting.Name] = prop.Value.GetString()!;
                }
            }
        }

        return result;
    }
}
