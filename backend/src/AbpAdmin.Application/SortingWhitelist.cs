using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AbpAdmin;

/// <summary>
/// Dynamic LINQ 排序串（PagedAndSortedResultRequestDto.Sorting）白名单校验：
/// 未校验的排序串直接进 OrderBy 会被 System.Linq.Dynamic.Core 解析，
/// 注入载荷（分号拼接表达式、构造函数调用等）解析失败走全局异常处理器返回 500——
/// 语义应为请求校验错（400）。规则：逗号分隔的「字段名 [asc|desc]」，字段名大小写不敏感。
/// </summary>
public static partial class SortingWhitelist
{
    [GeneratedRegex("^(asc|desc)$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectionRegex();

    /// <summary>
    /// 校验排序串。null/空白合法（由调用方决定默认排序）；
    /// 允许形如 "ExecutionTime desc, UserName"、"duration ASC"（字段名/方向均忽略大小写）。
    /// </summary>
    public static bool IsValid(string? sorting, IReadOnlyCollection<string> allowedFields)
    {
        if (string.IsNullOrWhiteSpace(sorting))
        {
            return true;
        }

        foreach (var rawSegment in sorting.Split(','))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                return false;
            }

            // 按第一个空白切字段名与可选方向（连续空白也只认一处）
            var parts = segment.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries);
            var fieldName = parts[0];

            var matched = false;
            foreach (var allowed in allowedFields)
            {
                if (string.Equals(allowed, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }

            if (parts.Length == 2 && !DirectionRegex().IsMatch(parts[1].Trim()))
            {
                return false;
            }
        }

        return true;
    }
}
