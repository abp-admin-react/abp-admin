using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using Volo.Abp.Validation;

namespace AbpAdmin.Grids;

/// <summary>
/// 资源将公开的列键绑定到类型化表达式。使用原查询提供程序执行筛选、排序，支持实体和数据库聚合投影。
/// </summary>
public sealed partial class GridQueryDefinition<T>
{
    private sealed record Column(LambdaExpression Selector, string[] Operators);
    private readonly Dictionary<string, Column> _columns = new(StringComparer.Ordinal);

    public GridQueryDefinition<T> Field<TKey>(string key, Expression<Func<T, TKey>> selector, params string[] allowedOperators)
    {
        _columns.Add(key, new Column(selector, allowedOperators));
        return this;
    }

    public IQueryable<T> Filter(IQueryable<T> query, GridQueryInput input)
    {
        if (input.SkipCount < 0 || input.MaxResultCount is < 1 or > 200)
        {
            throw Invalid("pagination", "偏移量须为非负整数，每页允许 1 至 200 条记录。");
        }
        if (input.Keyword?.Length > 512 || input.Filters is null || input.Filters.Count > 32 ||
            input.Sort is null || input.Sort.Count > 16)
        {
            throw Invalid("query", "关键字、筛选组或排序列超出限制。");
        }

        // 在执行总数查询前拒绝不允许的排序字段和方向。
        var sortedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sort in input.Sort)
        {
            if (sort is null || string.IsNullOrEmpty(sort.Field) || !_columns.ContainsKey(sort.Field) ||
                sort.Direction is not ("asc" or "desc") || !sortedFields.Add(sort.Field))
            {
                throw Invalid("sort", "排序字段或方向无效，或同一列重复排序。");
            }
        }

        foreach (var group in input.Filters)
        {
            if (group is null || string.IsNullOrEmpty(group.Field) || !_columns.TryGetValue(group.Field, out var column))
            {
                throw Invalid("filters", "不支持此筛选列。");
            }
            if (group.Join is not ("and" or "or") || group.Conditions is null || group.Conditions.Count is < 1 or > 16)
            {
                throw Invalid(group.Field, "连接运算符或筛选条件数量无效。");
            }

            Expression? groupBody = null;
            foreach (var condition in group.Conditions)
            {
                if (condition is null || condition.Value is null || condition.Value.Length > 1024 ||
                    condition.ValueTo?.Length > 1024 ||
                    (column.Operators.Length != 0 && !column.Operators.Contains(condition.Op)))
                {
                    throw Invalid(group.Field, "筛选运算符或输入无效。");
                }
                var test = BuildComparison(column.Selector.Body, group.Field, condition);
                groupBody = groupBody == null ? test :
                    group.Join == "or" ? Expression.OrElse(groupBody, test) : Expression.AndAlso(groupBody, test);
            }
            query = query.Where(Expression.Lambda<Func<T, bool>>(groupBody!, column.Selector.Parameters));
        }
        return query;
    }

    public IOrderedQueryable<T> OrderBy<TKey>(IQueryable<T> query, GridQueryInput input, string defaultField,
        bool defaultDescending, Expression<Func<T, TKey>> stableKey)
    {
        IOrderedQueryable<T>? ordered = null;
        foreach (var sort in input.Sort)
        {
            ordered = ApplyOrder(ordered ?? query, _columns[sort.Field].Selector,
                sort.Direction == "desc", ordered != null);
        }
        ordered ??= ApplyOrder(query, _columns[defaultField].Selector, defaultDescending, false);
        // 唯一键打破并列。业务值相等时翻页仍有确定的顺序。
        return ordered.ThenBy(stableKey);
    }

    private static IOrderedQueryable<T> ApplyOrder(IQueryable<T> query, LambdaExpression selector, bool desc, bool append)
    {
        var method = (append, desc) switch
        {
            (false, false) => nameof(Queryable.OrderBy),
            (false, true) => nameof(Queryable.OrderByDescending),
            (true, false) => nameof(Queryable.ThenBy),
            (true, true) => nameof(Queryable.ThenByDescending)
        };
        var call = Expression.Call(typeof(Queryable), method, [typeof(T), selector.ReturnType],
            query.Expression, Expression.Quote(selector));
        return (IOrderedQueryable<T>)query.Provider.CreateQuery<T>(call);
    }

    private static AbpValidationException Invalid(string field, string message) =>
        new(message, [new ValidationResult(message, [field])]);
}
