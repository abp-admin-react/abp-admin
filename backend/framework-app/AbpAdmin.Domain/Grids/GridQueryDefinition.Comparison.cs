using System;
using System.Globalization;
using System.Linq.Expressions;

namespace AbpAdmin.Grids;

public sealed partial class GridQueryDefinition<T>
{
    private static Expression BuildComparison(Expression member, string field, GridCondition condition)
    {
        var (op, value, end) = (condition.Op, condition.Value, condition.ValueTo);
        if ((op == "between") != (end != null))
        {
            throw Invalid(field, "区间筛选需要且仅接受两个端点。");
        }

        var nullableType = Nullable.GetUnderlyingType(member.Type);
        var type = nullableType ?? member.Type;
        var actual = nullableType == null ? member : Expression.Property(member, "Value");
        Expression test;
        if (type == typeof(string))
        {
            var lower = Expression.Call(actual, nameof(string.ToLower), Type.EmptyTypes);
            var term = Parameter(value.ToLowerInvariant());
            test = op switch
            {
                "eq" => Expression.Equal(lower, term),
                "ne" => Expression.NotEqual(lower, term),
                "contains" => Expression.Call(lower, nameof(string.Contains), null, term),
                "notContains" => Expression.Not(Expression.Call(lower, nameof(string.Contains), null, term)),
                "startsWith" => Expression.Call(lower, nameof(string.StartsWith), null, term),
                "endsWith" => Expression.Call(lower, nameof(string.EndsWith), null, term),
                _ => throw Invalid(field, "文本列不支持此运算符。")
            };
        }
        else if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            test = CompareDay(actual, op, value, end, field);
        }
        else
        {
            var ordered = type == typeof(decimal) || type == typeof(int) || type == typeof(long) ||
                type == typeof(byte) || type == typeof(short) || type == typeof(double);
            if (!ordered && op is not ("eq" or "ne"))
            {
                throw Invalid(field, "此列仅支持相等或不等比较。");
            }
            // 枚举比较使用其存储类型；接受已定义的名称或数值。
            if (type.IsEnum)
            {
                if (!Enum.TryParse(type, value, true, out var parsed) || !Enum.IsDefined(type, parsed))
                    throw Invalid(field, "无效的枚举值。");
                var underlying = Enum.GetUnderlyingType(type);
                value = Convert.ChangeType(parsed, underlying, CultureInfo.InvariantCulture)!.ToString()!;
                actual = Expression.Convert(actual, underlying);
                type = underlying;
            }
            var first = Parse(type, value, field);
            test = op switch
            {
                "eq" => Expression.Equal(actual, first),
                "ne" => Expression.NotEqual(actual, first),
                "gt" => Expression.GreaterThan(actual, first),
                "gte" => Expression.GreaterThanOrEqual(actual, first),
                "lt" => Expression.LessThan(actual, first),
                "lte" => Expression.LessThanOrEqual(actual, first),
                "between" => Expression.AndAlso(Expression.GreaterThanOrEqual(actual, first),
                    Expression.LessThanOrEqual(actual, Parse(type, end!, field))),
                _ => throw Invalid(field, "此列不支持该筛选运算符。")
            };
        }
        // 所有比较均排除空单元格，包括“不等于”和“不包含”。
        return nullableType != null || type == typeof(string)
            ? Expression.AndAlso(Expression.NotEqual(member, Expression.Constant(null, member.Type)), test)
            : test;
    }

    private static Expression CompareDay(Expression member, string op, string value, string? end, string field)
    {
        DateTime Read(string text) =>
            DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)
                ? day : throw Invalid(field, "日期须使用 yyyy-MM-dd（UTC）。");

        // UTC 日范围使用半开区间，保留索引可用性及数据库时间戳的子秒精度。
        Expression At(DateTime day) => member.Type == typeof(DateTimeOffset)
            ? Parameter(new DateTimeOffset(day)) : Parameter(day);
        Expression Through(DateTime day) => day == DateTime.MaxValue.Date
            ? Expression.Constant(true) : Expression.LessThan(member, At(day.AddDays(1)));
        var start = Read(value);
        var atOrAfter = Expression.GreaterThanOrEqual(member, At(start));
        return op switch
        {
            "eq" => Expression.AndAlso(atOrAfter, Through(start)),
            "ne" => Expression.Not(Expression.AndAlso(atOrAfter, Through(start))),
            "lt" => Expression.LessThan(member, At(start)),
            "lte" => Through(start),
            "gt" => Expression.Not(Through(start)),
            "gte" => atOrAfter,
            "between" => Expression.AndAlso(atOrAfter, Through(Read(end!))),
            _ => throw Invalid(field, "日期列不支持此运算符。")
        };
    }

    private static Expression Parse(Type type, string text, string field)
    {
        var culture = CultureInfo.InvariantCulture;
        if (type == typeof(decimal) && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                culture, out var money))
        {
            // 超出 decimal 表示精度的输入可能被 TryParse 静默舍入。金额筛选必须精确。
            var parts = text.TrimStart('+', '-').Split('.');
            var integral = parts[0].TrimStart('0');
            var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
            var canonical = (integral.Length == 0 ? "0" : integral) + (fraction.Length > 0 ? "." + fraction : "");
            if (decimal.Abs(money).ToString("0.############################", culture) == canonical)
            {
                return Parameter(money);
            }
        }
        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, culture, out var integer))
            return Parameter(integer);
        if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, culture, out var longValue))
            return Parameter(longValue);
        if (type == typeof(byte) && byte.TryParse(text, NumberStyles.Integer, culture, out var octet))
            return Parameter(octet);
        if (type == typeof(short) && short.TryParse(text, NumberStyles.Integer, culture, out var shortValue))
            return Parameter(shortValue);
        if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, culture, out var floatingPoint) && double.IsFinite(floatingPoint))
            return Parameter(floatingPoint);
        if (type == typeof(bool) && bool.TryParse(text, out var boolean))
            return Parameter(boolean);
        if (type == typeof(Guid) && Guid.TryParseExact(text, "D", out var id))
            return Parameter(id);
        throw Invalid(field, "筛选值的类型、格式或精度无效。");
    }

    // 闭包表达式使 EF 将用户输入作为查询参数，避免按每个筛选值生成不同的查询计划。
    private static Expression Parameter<TValue>(TValue value)
    {
        Expression<Func<TValue>> bound = () => value;
        return bound.Body;
    }
}
