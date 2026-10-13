using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AbpAdmin.Desensitization;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 属性级差异对比（对标 mzt-biz-log 的 _DIFF 函数 + @DiffLogField）。
/// 仅比较可读的标量属性（基元/字符串/枚举/日期/Guid 及其可空形式），
/// 复杂类型属性跳过——语义日志要的是「业务人员可读的变化摘要」，不是完整 patch。
/// </summary>
public static class OperationLogDiffer
{
    private const int MaxValueLength = 128;

    /// <summary>
    /// 对比两个值，输出「【显示名】旧值 → 新值」以「；」连接的摘要；无差异返回 null。
    /// 入参是标量时直接输出单条「旧值 → 新值」；是对象时枚举其标量属性逐项对比。
    /// 旧值为 null 时按「全部新增」输出。
    /// </summary>
    public static string? Diff(object? oldValue, object? newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
        {
            return null;
        }

        var targetType = (newValue ?? oldValue)!.GetType();

        if (IsComparableType(targetType))
        {
            var oldScalar = FormatValue(oldValue);
            var newScalar = FormatValue(newValue);
            return string.Equals(oldScalar ?? "", newScalar ?? "", StringComparison.Ordinal)
                ? null
                : $"{Truncate(oldScalar)} → {Truncate(newScalar)}";
        }

        var changes = targetType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsComparableType(p.PropertyType))
            .Select(p => new
            {
                Name = GetDisplayName(p),
                Old = oldValue == null ? null : SafeGet(oldValue, p.Name),
                New = newValue == null ? null : SafeGet(newValue, p.Name),
            })
            .Where(x => !string.Equals(x.Old ?? "", x.New ?? "", StringComparison.Ordinal))
            .Select(x => $"【{x.Name}】{Truncate(x.Old)} → {Truncate(x.New)}")
            .ToList();

        return changes.Count == 0 ? null : string.Join("；", changes);
    }

    private static string? GetDisplayName(PropertyInfo property)
    {
        return property.GetCustomAttribute<OperationLogFieldAttribute>()?.DisplayName ?? property.Name;
    }

    /// <summary>
    /// 在实例自己的类型上解析同名属性取值——不能拿 targetType 的 PropertyInfo 直接读另一个类型的实例：
    /// _diff 的两侧通常是不同类型（匿名旧值快照 vs 更新 DTO），跨类型 GetValue 抛 ArgumentException
    /// 被 catch 吞成 null，表现为旧值全部「(空)」（浏览器实测发现的回归）。
    /// </summary>
    private static string? SafeGet(object instance, string propertyName)
    {
        try
        {
            var property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null)
            {
                return null;
            }

            var value = FormatValue(property.GetValue(instance));
            // 带 [Masked] 的属性值脱敏后进 diff：防止未来对含机密字段（密码/连接串）的 DTO 做
            // _diff 时把明文旧值→新值写进操作日志（安全审查：diff 无脱敏钩子）
            var maskSpec = property.GetCustomAttribute<MaskedAttribute>();
            return maskSpec == null || value == null ? value : StringMasker.Mask(value, maskSpec);
        }
        catch
        {
            // getter 抛异常不应影响整条日志
            return null;
        }
    }

    internal static string? FormatValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            bool b => b ? "是" : "否",
            DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset offset => offset.ToString("yyyy-MM-dd HH:mm:ss"),
            IEnumerable enumerable and not string => FormatEnumerable(enumerable),
            _ when IsComparableType(value.GetType()) => value.ToString(),
            _ => null, // 复杂类型不进差异摘要
        };
    }

    /// <summary>集合值逗号连接（上限 20 项，超出加省略号），元素取标量字符串。</summary>
    private static string FormatEnumerable(IEnumerable enumerable)
    {
        const int maxItems = 20;
        var items = enumerable.Cast<object?>()
            .Take(maxItems + 1)
            .Select(item => item == null || !IsComparableType(item.GetType()) ? item?.ToString() : FormatValue(item))
            .ToList();

        var suffix = items.Count > maxItems ? "…" : "";
        return string.Join(",", items.Take(maxItems)) + suffix;
    }

    private static string Truncate(string? value)
    {
        const string ellipsis = "…";
        if (value == null)
        {
            return "(空)";
        }

        return value.Length <= MaxValueLength ? value : value[..(MaxValueLength - 1)] + ellipsis;
    }

    private static bool IsComparableType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime)
               || underlying == typeof(DateTimeOffset)
               || underlying == typeof(TimeSpan)
               || underlying == typeof(Guid)
               || underlying.IsEnum;
    }
}
