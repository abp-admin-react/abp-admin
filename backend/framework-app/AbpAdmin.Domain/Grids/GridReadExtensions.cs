using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Volo.Abp.Linq;

namespace AbpAdmin.Grids;

public static class GridReadExtensions
{
    /// <summary>在调用方提供的可见数据集上筛选并计数，稳定排序后仅映射当前页。</summary>
    public static async Task<(long TotalCount, List<TDto> Items)> ReadPageAsync<T, TKey, TDto>(
        this GridQueryDefinition<T> grid, IQueryable<T> query, GridQueryInput input,
        IAsyncQueryableExecuter executer, string defaultField, bool descending,
        Expression<Func<T, TKey>> stableKey, Func<T, TDto> map)
    {
        var filtered = grid.Filter(query, input);
        var count = await executer.LongCountAsync(filtered);
        var ordered = grid.OrderBy(filtered, input, defaultField, descending, stableKey);
        var rows = await executer.ToListAsync(ordered.Skip(input.SkipCount).Take(input.MaxResultCount));
        return (count, rows.Select(map).ToList());
    }
}
