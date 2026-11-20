using System;
using System.Linq.Expressions;

namespace AbpAdmin.Grids;

public sealed partial class GridQueryDefinition<T>
{
    /// <summary>枚举名称以 CASE 表达式交给数据库比较，避免 int 存储的枚举被转成数字字符串。</summary>
    public GridQueryDefinition<T> SearchableEnumField<TValue>(string key, Expression<Func<T, TValue>> selector)
    {
        var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (!type.IsEnum)
            throw new ArgumentException("列必须映射枚举值。", nameof(selector));

        Expression name = Expression.Constant(null, typeof(string));
        foreach (var value in Enum.GetValues(type))
        {
            Expression member = Expression.Constant(value, type);
            if (type != typeof(TValue)) member = Expression.Convert(member, typeof(TValue));
            name = Expression.Condition(Expression.Equal(selector.Body, member),
                Expression.Constant(Enum.GetName(type, value), typeof(string)), name);
        }
        return SearchableField(key, Expression.Lambda<Func<T, string?>>(name, selector.Parameters));
    }
}
