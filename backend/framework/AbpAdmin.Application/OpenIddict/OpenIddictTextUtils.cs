using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// OpenIddict 模块的文本/JSON 解析工具。
/// 「按 \r \n , ; 分割列表」的分隔符约定是前后端共同依赖的隐式契约
/// （前端 applications 页的 /[\r\n,;]+/ 正则与之对应），统一在此处实现，避免双份漂移。
/// </summary>
internal static class OpenIddictTextUtils
{
    /// <summary>
    /// 按换行/逗号/分号分割多行文本输入（Trim + 去空白行），null 安全。
    /// </summary>
    public static IEnumerable<string> SplitList(string? value)
    {
        return (value ?? string.Empty)
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !x.IsNullOrWhiteSpace());
    }

    /// <summary>
    /// 把 OpenIddict 实体的 JSON 数组字段（Permissions/Requirements）解析为字符串列表；
    /// 空/非法 JSON 返回空列表而不是抛异常（历史数据容错）。
    /// </summary>
    public static List<string> ParseJsonArray(string? json)
    {
        if (json.IsNullOrWhiteSpace())
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json!) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
