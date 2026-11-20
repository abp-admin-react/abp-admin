using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Grids;

internal static class GridSnapshotExtensions
{
    /// <summary>身份管理器或运行时注册表返回完整集合时，在服务端筛选排序，只把当前页发给客户端。</summary>
    internal static PagedResultDto<T> ReadSnapshot<T, TKey>(this GridQueryDefinition<T> grid,
        IEnumerable<T> rows, GridQueryInput input, string defaultField, Expression<Func<T, TKey>> stableKey)
    {
        var matches = grid.Filter(rows.AsQueryable(), input).ToList();
        var ordered = grid.OrderBy(matches.AsQueryable(), input, defaultField, false, stableKey);
        return new(matches.Count, ordered.Skip(input.SkipCount).Take(input.MaxResultCount).ToList());
    }
}
