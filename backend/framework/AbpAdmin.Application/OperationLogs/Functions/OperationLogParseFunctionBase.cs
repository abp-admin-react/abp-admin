using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// ID→名称 解析函数基类：统一处理缓存（含负结果缓存）、集合入参、未找到兜底与异常防线。
/// 子类只需提供函数名与按 Id 查名称的单点实现。
/// </summary>
public abstract class OperationLogParseFunctionBase : IOperationLogParseFunction
{
    /// <summary>
    /// 名称缓存 TTL：实体改名/删除后历史语义日志允许有最长 1 小时的名称滞后，
    /// 换取高频函数（user 等）不打穿数据库。
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private const int MaxCollectionItems = 20;

    private readonly IDistributedCache<OperationLogNameCacheItem> _cache;

    protected OperationLogParseFunctionBase(IDistributedCache<OperationLogNameCacheItem> cache)
    {
        _cache = cache;
    }

    public abstract string Name { get; }

    public virtual async ValueTask<string?> ResolveAsync(object? value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            // string 也是 IEnumerable，必须先排除，否则按字符集解析
            if (value is string or not IEnumerable)
            {
                return await ResolveSingleAsync(value);
            }

            // 手工迭代以支持「恰好 20 项不误加省略号」：到达上限时先探测是否还有剩余元素。
            // 非泛型 IEnumerator 不保证 IDisposable，手动按需释放。
            var names = new List<string>();
            var enumerator = ((IEnumerable)value).GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    var name = await ResolveSingleAsync(enumerator.Current);
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }

                    if (names.Count >= MaxCollectionItems && enumerator.MoveNext())
                    {
                        names.Add("…");
                        break;
                    }
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }

            return string.Join(", ", names);
        }
        catch
        {
            // 解析函数故障不传染日志链路（引擎侧还有一道防线）
            return null;
        }
    }

    private async ValueTask<string?> ResolveSingleAsync(object single)
    {
        if (!TryGetGuid(single, out var id))
        {
            // 模板把非 ID 值（如普通字符串）喂给了函数：原样渲染，不静默丢信息
            return OperationLogDiffer.FormatValue(single);
        }

        var cacheKey = $"abpadmin:operation-log-name:{Name}:{id}";
        var cached = await _cache.GetAsync(cacheKey);
        if (cached?.Name != null)
        {
            return cached.Name;
        }

        var name = await FindNameAsync(id);
        if (string.IsNullOrEmpty(name))
        {
            name = $"未知({id.ToString()[..8]})";
        }

        await _cache.SetAsync(
            cacheKey,
            new OperationLogNameCacheItem { Name = name },
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        return name;
    }

    /// <summary>标准 TryParse 契约：true 时 id 必为有效值。</summary>
    private static bool TryGetGuid(object value, out Guid id)
    {
        if (value is Guid guid)
        {
            id = guid;
            return true;
        }

        return Guid.TryParse(value.ToString(), out id);
    }

    /// <summary>按 Id 查实体可读名称；查不到返回 null（由基类统一兜底为「未知(前8位)」）。</summary>
    protected abstract ValueTask<string?> FindNameAsync(Guid id);
}
