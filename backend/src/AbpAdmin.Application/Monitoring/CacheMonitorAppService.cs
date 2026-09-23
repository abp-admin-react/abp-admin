using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.Caching;

namespace AbpAdmin.Monitoring;

/// <summary>
/// 缓存监控（对标 RuoYi「缓存监控」）。排障用：按前缀浏览键、看值、删键。
/// 只允许操作 <see cref="AbpDistributedCacheOptions.KeyPrefix"/>（默认 c:）下的键，
/// 防止共享 Redis 上误删其他系统的数据。浏览走 SCAN（增量、不阻塞），绝不 KEYS。
/// Memory 后端（开发默认）无法枚举键：Microsoft 的 MemoryDistributedCache 没有枚举入口，
/// 页面据此提示切 Redis 才可浏览。
/// </summary>
[Authorize(AbpAdminPermissions.CacheMonitor.Default)]
public class CacheMonitorAppService : AbpAdminAppService, ICacheMonitorAppService
{
    /// <summary>单键值预览的截断上限（字符数）。</summary>
    protected const int ContentPreviewLimit = 8 * 1024;

    /// <summary>集合类型（List/Set/SortedSet）值预览的条数上限。</summary>
    protected const int CollectionPreviewLimit = 100;

    private readonly CacheMonitorRedisConnection _redisConnection;
    private readonly AbpDistributedCacheOptions _cacheOptions;

    public CacheMonitorAppService(
        CacheMonitorRedisConnection redisConnection,
        IOptions<AbpDistributedCacheOptions> cacheOptions)
    {
        _redisConnection = redisConnection;
        _cacheOptions = cacheOptions.Value;
    }

    public virtual async Task<CacheMonitorInfoDto> GetInfoAsync()
    {
        var dto = new CacheMonitorInfoDto
        {
            Backend = "memory",
            KeyPrefix = _cacheOptions.KeyPrefix
        };

        if (_redisConnection.IsEnabled)
        {
            dto.Backend = "redis";
            try
            {
                var database = await GetDatabaseAsync();
                // 注：GetEndPoints()[0] 在集群/哨兵拓扑下不一定是可查询节点，此处按
                // 单实例/主从常规部署取第一个端点（监控页只读，错了由下方 catch 兜底亮出原因）
                var server = database.Multiplexer.GetServer(database.Multiplexer.GetEndPoints()[0]);
                dto.TotalKeys = await server.DatabaseSizeAsync(database.Database);

                var sections = await server.InfoAsync();
                foreach (var entry in sections.SelectMany(section => section))
                {
                    switch (entry.Key)
                    {
                        case "redis_version":
                            dto.RedisVersion = entry.Value;
                            break;
                        case "used_memory":
                            dto.UsedMemoryBytes = long.TryParse(entry.Value, out var used) ? used : null;
                            break;
                        case "maxmemory":
                            dto.MaxMemoryBytes = long.TryParse(entry.Value, out var max) ? max : null;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                // 连不上不炸整个页面：把原因亮给监控页（此时键浏览/删除自然也不可用）
                dto.ConnectionError = ex.Message;
            }
        }

        return dto;
    }

    public virtual async Task<CacheKeyListResultDto> GetKeysAsync(string? prefix, long cursor, int maxResultCount)
    {
        var database = await RequireDatabaseAsync();
        maxResultCount = Math.Clamp(maxResultCount, 1, 200);

        var pattern = "*" + (prefix ?? string.Empty) + "*";

        // 单次 SCAN 只是"扫描提示"，返回的键可能多于 maxResultCount：旧实现 .Take(N) 截断后
        // 游标已前进，溢出键跨页永久丢失。这里循环 SCAN 攒够一页，溢出键 + 游标留在
        // 进程内缓冲（PageBuffer），下一页请求带合成游标来时续上（round1 审查问题，round2 预审方案）。
        List<string> keys;
        long scanCursor;

        if (cursor == 0)
        {
            // 首页：从头开始
            keys = new List<string>();
            scanCursor = 0;
        }
        else if (PageBuffer.TryRemove(cursor, out var buffered) && buffered.Pattern == pattern)
        {
            // 续页且缓冲命中：上一页溢出的键先入页，再从缓冲的游标继续 SCAN
            keys = buffered.OverflowKeys;
            scanCursor = buffered.ScanCursor;
        }
        else if (cursor < 0)
        {
            // 负数是本类发出去的合成游标，命中不了缓冲（缓冲被清/进程重启）就是死页签：
            // 从哪续已不可知，如实返回空页并结束，避免拿负数游标打 Redis 报错
            return new CacheKeyListResultDto { Keys = new List<CacheKeyDto>(), NextCursor = 0 };
        }
        else
        {
            // 正数原始游标但缓冲未命中（多实例无粘性时请求落到别的实例）：退化为旧行为，
            // 从该真实游标重新 SCAN——溢出键不可恢复，但游标语义保证之后的键仍不重不漏
            keys = new List<string>();
            scanCursor = cursor;
        }

        // COUNT 是每次迭代的扫描提示（不保证返回条数），攒够一页或扫完（游标归零）为止。
        // 首页（cursor==0 进入）必须先无条件 SCAN 一次：SCAN 语义里游标 0 是"从头开始"而非"已结束"，
        // 若用 while 且 scanCursor 初值 0，循环一次都不执行 → 首页恒空、NextCursor=0（round3 lens 发现的回归）。
        if (cursor == 0)
        {
            var first = await ScanPageAsync(database, 0, pattern, maxResultCount);
            scanCursor = first.Cursor;
            keys.AddRange(first.Keys);
        }

        // MATCH 命中稀疏时（冷门前缀）可能要翻完整个键空间才凑满一页——加迭代上限：
        // 到顶即停，返回已凑到的键 + 真实游标（客户端从该游标续扫，语义不重不漏）
        var maxScanIterations = 200;
        while (keys.Count < maxResultCount && scanCursor != 0 && maxScanIterations-- > 0)
        {
            var result = await ScanPageAsync(database, scanCursor, pattern, maxResultCount);
            scanCursor = result.Cursor;
            keys.AddRange(result.Keys);
        }

        var page = keys.Take(maxResultCount).ToList();
        var overflow = keys.Skip(maxResultCount).ToList();

        long nextCursor;
        if (overflow.Count > 0)
        {
            // 有溢出键时不能把真实游标直接当 NextCursor（客户端下次带它来会重扫这一段、
            // 溢出键又被丢一遍）：发一个负数合成游标（SCAN 游标恒非负，不会撞），
            // 映射到缓冲里的 {溢出键, 真实游标}
            nextCursor = Interlocked.Decrement(ref _pageCursorSeed);
            PageBuffer[nextCursor] = (pattern, overflow, scanCursor);

            // 防泄漏兜底：用户中途关页会留死条目，超容量整体清空（清空只是让在途翻页
            // 落到上面"缓冲未命中"分支，代价可接受）
            if (PageBuffer.Count > PageBufferCapacity)
            {
                PageBuffer.Clear();
            }
        }
        else
        {
            // 无溢出：真实游标本身已含全部状态，不需要缓冲
            nextCursor = scanCursor;
        }

        // 每键 3 次往返（TYPE/TTL/MEMORY USAGE）合到一个 IBatch 里发，
        // 一批 50 键从 150+ 次 RTT 降为 1 次批往返。
        // Execute 保持同步是有据可查的取舍：StackExchange.Redis 的 IBatch 只有 void Execute()，
        // 没有 ExecuteAsync()（已对照 2.7.33 与 2.9.x 反编译核实）；Execute 只是把已排队
        // 的异步命令冲刷出去，命令结果全部由下方 await 的任务异步消费，不在 I/O 上阻塞线程
        var batch = database.CreateBatch();
        var typeTasks = page.Select(k => batch.KeyTypeAsync(k)).ToList();
        var ttlTasks = page.Select(k => batch.KeyTimeToLiveAsync(k)).ToList();
        var sizeTasks = page.Select(k => GetMemoryUsageAsync(batch, k)).ToList();
        batch.Execute();
        await Task.WhenAll(typeTasks.Select(t => (Task)t)
            .Concat(ttlTasks.Select(t => (Task)t))
            .Concat(sizeTasks.Select(t => (Task)t)));

        var dtos = new List<CacheKeyDto>(page.Count);
        for (var i = 0; i < page.Count; i++)
        {
            var type = await typeTasks[i];
            var ttl = await ttlTasks[i];
            dtos.Add(new CacheKeyDto
            {
                Key = page[i],
                Type = type.ToString().ToLowerInvariant(),
                SizeBytes = await sizeTasks[i],
                TtlSeconds = ttl == null ? null : (long)Math.Ceiling(ttl.Value.TotalSeconds)
            });
        }

        return new CacheKeyListResultDto
        {
            Keys = dtos,
            NextCursor = nextCursor
        };
    }

    /// <summary>
    /// SCAN 分页的进程内溢出缓冲：key = 返回给客户端的合成游标（负数），
    /// value = (匹配模式, 溢出键, 真实 SCAN 游标)。AppService 是瞬态的，缓冲必须 static。
    /// 仅单实例有效：多实例部署且负载均衡无粘性时，续页请求可能落到别的实例而命中不了
    /// 缓冲——那时自动退化为旧行为（丢溢出键，游标继续），排障场景可接受。
    /// </summary>
    private static readonly ConcurrentDictionary<long, (string Pattern, List<string> OverflowKeys, long ScanCursor)> PageBuffer = new();

    /// <summary>合成游标发号器：从 -1 起递减，负数与 SCAN 的非负真实游标永不相撞。</summary>
    private static long _pageCursorSeed;

    /// <summary>缓冲容量上限：翻页走完的条目会即时移除，超限说明有大量中途放弃的翻页，整体清空。</summary>
    private const int PageBufferCapacity = 128;

    public virtual async Task<CacheValueDto> GetValueAsync(string key)
    {
        AssertAbpKey(key);
        var database = await RequireDatabaseAsync();

        var type = await database.KeyTypeAsync(key);
        var ttl = await database.KeyTimeToLiveAsync(key);
        var builder = new StringBuilder();
        var truncated = new TruncationFlag();

        switch (type)
        {
            case RedisType.String:
                var value = await database.StringGetAsync(key);
                AppendPreview(builder, value.ToString(), ref truncated.Value);
                break;

            case RedisType.Hash:
                await AppendHashPreviewAsync(database, key, builder, truncated);
                break;

            case RedisType.List:
                await AppendListPreviewAsync(database, key, builder, truncated);
                break;

            case RedisType.Set:
                await AppendSetPreviewAsync(database, key, builder, truncated);
                break;

            case RedisType.SortedSet:
                await AppendSortedSetPreviewAsync(database, key, builder, truncated);
                break;

            default:
                builder.Append($"（{type} 类型不支持内容预览）");
                break;
        }

        return new CacheValueDto
        {
            Key = key,
            Type = type.ToString().ToLowerInvariant(),
            TtlSeconds = ttl == null ? null : (long)Math.Ceiling(ttl.Value.TotalSeconds),
            Content = builder.ToString(),
            Truncated = truncated.Value
        };
    }

    /// <summary>
    /// 异步方法不能带 ref 参数，截断标记用引用语义的小包装在 await 边界间传递。
    /// </summary>
    private sealed class TruncationFlag
    {
        public bool Value;
    }

    [Authorize(AbpAdminPermissions.CacheMonitor.Manage)]
    public virtual async Task DeleteKeyAsync(string key)
    {
        AssertAbpKey(key);
        var database = await RequireDatabaseAsync();
        await database.KeyDeleteAsync(key);
    }

    protected virtual async Task<IDatabase> GetDatabaseAsync()
    {
        var multiplexer = await _redisConnection.GetMultiplexerAsync()
            ?? throw new InvalidOperationException(
                _redisConnection.LastConnectError is { Length: > 0 } error
                    ? $"Redis 连接失败：{error}"
                    : "Redis 缓存未启用。");
        return multiplexer.GetDatabase();
    }

    protected virtual async Task<IDatabase> RequireDatabaseAsync()
    {
        if (!_redisConnection.IsEnabled)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorRedisDisabled)
                .WithData("Reason", "当前缓存后端是 Memory，无法枚举键。请在启用 Redis 后使用本功能。");
        }

        return await GetDatabaseAsync();
    }

    /// <summary>
    /// 只放行 ABP 缓存键，防止共享 Redis 上误删其他系统的数据。
    /// 判定依据（rel-10.6 DistributedCacheKeyNormalizer 已核实）：
    /// KeyPrefix 配置非空 → 键必须以它开头；
    /// 为空（默认）→ 键必须以结构性标记开头：c:（宿主键）或 t:（租户键 t:{TenantId},c:...）。
    /// </summary>
    protected virtual void AssertAbpKey(string key)
    {
        var allowed = string.IsNullOrEmpty(_cacheOptions.KeyPrefix)
            ? key.StartsWith("c:", StringComparison.Ordinal) ||
              key.StartsWith("t:", StringComparison.Ordinal)
            : key.StartsWith(_cacheOptions.KeyPrefix, StringComparison.Ordinal);

        if (!allowed)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorKeyNotAllowed)
                .WithData("KeyPrefix", _cacheOptions.KeyPrefix is { Length: > 0 } prefix ? prefix : "c: / t:");
        }
    }

    protected static async Task<long?> GetMemoryUsageAsync(IDatabaseAsync database, string key)
    {
        try
        {
            var usage = await database.ExecuteAsync("MEMORY", "USAGE", key);
            return usage.IsNull ? null : (long)usage;
        }
        catch (RedisException)
        {
            // Redis < 4.0 或禁用了内存采样命令
            return null;
        }
    }

    /// <summary>
    /// 单次 SCAN 调用。游标 0 = 从头开始（合法首轮游标），返回游标 0 = 扫描结束。
    /// </summary>
    private static async Task<(long Cursor, List<string> Keys)> ScanPageAsync(
        IDatabaseAsync database, long cursor, string pattern, int countHint)
    {
        var result = await database.ExecuteAsync(
            "SCAN",
            cursor.ToString(),
            "MATCH", pattern,
            "COUNT", countHint.ToString());
        return (
            long.Parse((string)result[0]!),
            ((RedisResult[])result[1]!).Select(r => (string)r!).ToList());
    }

    private static async Task AppendHashPreviewAsync(
        IDatabaseAsync database, string key, StringBuilder builder, TruncationFlag truncated)
    {
        // HSCAN 只取首批条目，避免百万字段的大 Hash 整包物化（排障页恰恰最常被用来打开失控大键）
        var result = await database.ExecuteAsync(
            "HSCAN", key, "0", "COUNT", CollectionPreviewLimit.ToString());
        var flat = (RedisResult[])result[1]!;
        var pairs = new List<(string Name, string Value)>();
        for (var i = 0; i + 1 < flat.Length && pairs.Count < CollectionPreviewLimit; i += 2)
        {
            pairs.Add(((string)flat[i]!, (string)flat[i + 1]!));
        }

        foreach (var entry in pairs)
        {
            if (!TryAppendLine(builder, $"{entry.Name} = {entry.Value}", ref truncated.Value))
            {
                break;
            }
        }
    }

    private static async Task AppendListPreviewAsync(
        IDatabaseAsync database, string key, StringBuilder builder, TruncationFlag truncated)
    {
        // LRANGE 限量读取 + LLEN 取总数，避免大 List 整包物化
        var items = await database.ListRangeAsync(key, 0, CollectionPreviewLimit - 1);
        foreach (var item in items)
        {
            if (!TryAppendLine(builder, item.ToString(), ref truncated.Value))
            {
                break;
            }
        }

        var total = await database.ListLengthAsync(key);
        if (total > CollectionPreviewLimit && !truncated.Value)
        {
            builder.AppendLine($"…（共 {total} 项，仅展示前 {CollectionPreviewLimit}）");
        }
    }

    private static async Task AppendSetPreviewAsync(
        IDatabaseAsync database, string key, StringBuilder builder, TruncationFlag truncated)
    {
        // SSCAN 只取首批成员，避免大 Set 整包物化
        var result = await database.ExecuteAsync(
            "SSCAN", key, "0", "COUNT", CollectionPreviewLimit.ToString());
        var members = ((RedisResult[])result[1]!)
            .Take(CollectionPreviewLimit)
            .Select(r => (string)r!)
            .OrderBy(member => member, StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (!TryAppendLine(builder, member, ref truncated.Value))
            {
                break;
            }
        }
    }

    private static async Task AppendSortedSetPreviewAsync(
        IDatabaseAsync database, string key, StringBuilder builder, TruncationFlag truncated)
    {
        // ZRANGE(rank) 限量读取，避免大 ZSet 整包物化
        var sorted = await database.SortedSetRangeByRankWithScoresAsync(
            key, start: 0, stop: CollectionPreviewLimit - 1);
        foreach (var entry in sorted)
        {
            if (!TryAppendLine(builder, $"{entry.Element} (score: {entry.Score})", ref truncated.Value))
            {
                break;
            }
        }
    }

    private static void AppendPreview(StringBuilder builder, string content, ref bool truncated)
    {
        if (content.Length <= ContentPreviewLimit)
        {
            builder.Append(content);
            return;
        }

        builder.Append(content[..ContentPreviewLimit]);
        truncated = true;
    }

    private static bool TryAppendLine(StringBuilder builder, string line, ref bool truncated)
    {
        if (builder.Length + line.Length > ContentPreviewLimit)
        {
            truncated = true;
            return false;
        }

        builder.AppendLine(line);
        return true;
    }
}
