using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace AbpAdmin.Linq.DynamicQueryable;

/// <summary>
/// 动态查询条件 → 表达式树（移植自 abp-next-admin framework/dynamic-queryable 的
/// ObjectQueryableExtensions，适配点见 DynamicCondition 注释）。
/// 字段解析走 Expression.PropertyOrField：字段名必须真实存在于 T，天然免疫注入；
/// 值按属性真实类型转换（InvariantCulture），转换失败立即抛错——调用方应先把
/// 字段/值约束在白名单内（见各 AppService 的 available-fields 契约）。
/// </summary>
public static class ObjectQueryableExtensions
{
    /// <summary>
    /// 条件组 → 谓词。非空契约由 <see cref="DynamicQuery"/> 保证（空集合短路返回原查询），
    /// 这里对空输入 fail-closed 抛错——恒真哨兵表达式（0 == 0）在 EF 翻译下是噪音。
    /// Logic 语义：条件按顺序累计结合，<see cref="DynamicLogic.Or"/> 表示与前面累计结果相或，
    /// 后续 And 作用于 "(A ∨ B) ∧ C" 的整体（有测试钉住）。
    /// </summary>
    private static Expression<Func<T, bool>> BuildPredicate<T>(IReadOnlyList<DynamicCondition> conditions)
    {
        if (conditions.Count == 0)
        {
            throw new ArgumentException("条件组不能为空（空集合请直接返回原查询，见 DynamicQuery）。", nameof(conditions));
        }

        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? body = null;

        foreach (var condition in conditions)
        {
            var comparison = BuildComparison(parameter, condition);
            body = body == null
                ? comparison
                : condition.Logic == DynamicLogic.Or
                    ? Expression.OrElse(body, comparison)
                    : Expression.AndAlso(body, comparison);
        }

        return Expression.Lambda<Func<T, bool>>(body!, parameter);
    }

    public static IQueryable<T> DynamicQuery<T>(this IQueryable<T> queryable, IReadOnlyList<DynamicCondition> conditions)
    {
        if (conditions.Count == 0)
        {
            return queryable;
        }

        return queryable.Where(BuildPredicate<T>(conditions));
    }

    private static Expression BuildComparison(Expression parameter, DynamicCondition condition)
    {
        // PropertyOrField 对不存在的字段直接抛 ArgumentException（fail-closed）
        var member = Expression.PropertyOrField(parameter, condition.Field);
        var propertyType = member.Type;

        switch (condition.Comparison)
        {
            case DynamicComparison.NotEqual:
                return Expression.NotEqual(member, ConvertValue(condition.Value, propertyType));

            case DynamicComparison.LessThan:
                return BuildOrderedComparison(condition, member, propertyType, Expression.LessThan, Expression.LessThan, Expression.LessThan);
            case DynamicComparison.LessThanOrEqual:
                return BuildOrderedComparison(condition, member, propertyType, Expression.LessThanOrEqual, Expression.LessThanOrEqual, Expression.LessThanOrEqual);
            case DynamicComparison.GreaterThan:
                return BuildOrderedComparison(condition, member, propertyType, Expression.GreaterThan, Expression.GreaterThan, Expression.GreaterThan);
            case DynamicComparison.GreaterThanOrEqual:
                return BuildOrderedComparison(condition, member, propertyType, Expression.GreaterThanOrEqual, Expression.GreaterThanOrEqual, Expression.GreaterThanOrEqual);

            case DynamicComparison.StartsWith:
                return Expression.Call(member, StringStartsWithMethod, ConvertValue(condition.Value, typeof(string)));
            case DynamicComparison.NotStartsWith:
                return Expression.Not(Expression.Call(member, StringStartsWithMethod, ConvertValue(condition.Value, typeof(string))));
            case DynamicComparison.EndsWith:
                return Expression.Call(member, StringEndsWithMethod, ConvertValue(condition.Value, typeof(string)));
            case DynamicComparison.NotEndsWith:
                return Expression.Not(Expression.Call(member, StringEndsWithMethod, ConvertValue(condition.Value, typeof(string))));
            case DynamicComparison.Contains:
                return Expression.Call(member, StringContainsMethod, ConvertValue(condition.Value, typeof(string)));
            case DynamicComparison.NotContains:
                return Expression.Not(Expression.Call(member, StringContainsMethod, ConvertValue(condition.Value, typeof(string))));

            case DynamicComparison.Null:
                // 非空引用/值类型以默认值表达「空」（与原实现一致）
                return Expression.Equal(member, Expression.Constant(GetDefaultValue(propertyType), propertyType));
            case DynamicComparison.NotNull:
                return Expression.NotEqual(member, Expression.Constant(GetDefaultValue(propertyType), propertyType));

            default:
            case DynamicComparison.Equal:
                return Expression.Equal(member, ConvertValue(condition.Value, propertyType));
        }
    }

    private static readonly MethodInfo StringStartsWithMethod =
        typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;
    private static readonly MethodInfo StringEndsWithMethod =
        typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;
    private static readonly MethodInfo StringContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

    /// <summary>
    /// 大小比较三分支（与原实现同构）：字符串走 CompareTo（SQL 侧翻译为字符串序），
    /// 可空类型先判 HasValue 再比 Value，普通类型直接比。
    /// </summary>
    private static Expression BuildOrderedComparison(
        DynamicCondition condition,
        MemberExpression member,
        Type propertyType,
        Func<MemberExpression, ConstantExpression, BinaryExpression> direct,
        Func<MethodCallExpression, ConstantExpression, BinaryExpression> compareTo,
        Func<MemberExpression, Expression, BinaryExpression> lifted)
    {
        if (propertyType == typeof(string))
        {
            return compareTo(
                Expression.Call(member, StringCompareToMethod, Expression.Constant(condition.Value ?? string.Empty)),
                Expression.Constant(0));
        }

        if (Nullable.GetUnderlyingType(propertyType) is { } underlyingType)
        {
            var hasValue = Expression.Property(member, nameof(Nullable<int>.HasValue));
            var value = Expression.Property(member, nameof(Nullable<int>.Value));

            return Expression.AndAlso(
                hasValue,
                lifted(value, ConvertValue(condition.Value, underlyingType)));
        }

        return direct(member, ConvertValue(condition.Value, propertyType));
    }

    private static readonly MethodInfo StringCompareToMethod =
        typeof(string).GetMethod("CompareTo", new[] { typeof(string) })!;

    /// <summary>字符串值 → 属性类型的常量表达式。可空类型转其底层类型（表达式树自动提升比较）。</summary>
    private static ConstantExpression ConvertValue(string? rawValue, Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        object? typedValue;

        if (rawValue == null)
        {
            typedValue = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }
        else if (targetType.IsEnum)
        {
            typedValue = Enum.Parse(targetType, rawValue, ignoreCase: true);
        }
        else if (targetType == typeof(Guid))
        {
            typedValue = Guid.Parse(rawValue);
        }
        else if (targetType == typeof(string))
        {
            typedValue = rawValue;
        }
        else
        {
            typedValue = Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
        }

        return Expression.Constant(typedValue, propertyType);
    }

    private static object? GetDefaultValue(Type type)
    {
        if (Nullable.GetUnderlyingType(type) != null || !type.IsValueType)
        {
            return null;
        }

        return Activator.CreateInstance(type);
    }
}
