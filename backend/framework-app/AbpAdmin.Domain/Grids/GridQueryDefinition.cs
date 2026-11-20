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
    private readonly ParameterExpression _keywordRow = Expression.Parameter(typeof(T), "row");
    private readonly List<Expression> _keywordFields = [];

    public GridQueryDefinition<T> Field<TKey>(string key, Expression<Func<T, TKey>> selector, params string[] allowedOperators)
    {
        _columns.Add(key, new Column(selector, allowedOperators));
        return this;
    }

    /// <summary>登记可参与跨列关键词匹配，同时可按列筛选和排序的字段。</summary>
    public GridQueryDefinition<T> SearchableField<TKey>(string key, Expression<Func<T, TKey>> selector, params string[] allowedOperators)
    {
        Field(key, selector, allowedOperators);
        return KeywordOnly(selector);
    }

    /// <summary>只登记关键词匹配表达式，不开放独立的列筛选或排序。字段由服务端根据权限选择。</summary>
    public GridQueryDefinition<T> KeywordOnly<TKey>(Expression<Func<T, TKey>> selector)
    {
        Expression field = new BindRow(selector.Parameters[0], _keywordRow).Visit(selector.Body);
        if ((Nullable.GetUnderlyingType(field.Type) ?? field.Type) == typeof(Guid))
            field = Expression.Call(field, nameof(Guid.ToString), Type.EmptyTypes);
        else if (field.Type != typeof(string))
            throw new ArgumentException("跨列模糊搜索仅支持文本和 UUID 表达式。", nameof(selector));
        _keywordFields.Add(field);
        return this;
    }

    private sealed class BindRow(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }

    /// <summary>任一登记字段包含完整关键词即命中。旧接口可选择区分大小写以保持其原有行为。</summary>
    public IQueryable<T> FilterKeyword(IQueryable<T> query, string? keyword, bool caseSensitive = false)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;
        if (_keywordFields.Count == 0)
            throw Invalid("keyword", "此表未配置可搜索的字段。");

        var term = Parameter(caseSensitive ? keyword : keyword.ToLowerInvariant());
        Expression? matches = null;
        foreach (var field in _keywordFields)
        {
            var text = caseSensitive ? field : Expression.Call(field, nameof(string.ToLower), Type.EmptyTypes);
            var contains = Expression.AndAlso(
                Expression.NotEqual(field, Expression.Constant(null, typeof(string))),
                Expression.Call(text, nameof(string.Contains), Type.EmptyTypes, term));
            matches = matches == null ? contains : Expression.OrElse(matches, contains);
        }
        return query.Where(Expression.Lambda<Func<T, bool>>(matches!, _keywordRow));
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

        query = FilterKeyword(query, input.Keyword?.Trim());
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
