using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
/// 只允许操作本应用的 ABP 键（结构前缀 c:/t:；配置了隔离前缀时还要求 k: 段含它），
/// 防止共享 Redis 上读写/误删其他系统的数据。浏览走 SCAN（增量、不阻塞），绝不 KEYS。
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

        if (!_redisConnection.IsEnabled)
        {
            return dto;
        }

        dto.Backend = "redis";

        IDatabase database;
        try
        {
            database = await GetDatabaseAsync();
        }
        catch (Exception ex)
        {
            // 连不上：此时键浏览/删除自然也不可用，把原因亮给监控页
            dto.ConnectionError = ex.Message;
            return dto;
        }

        // 以下概览统计（DBSIZE/INFO）与连接是两回事：共享实例以非 admin 模式运行时
        // 这些命令会被服务器拒绝，但连接与键浏览完全正常——单独归类，不误报成"连接失败"。
        // 两条命令都走 raw Execute：该实例的兼容层会让 SE.Redis 对部分"server 级" API
        // 在客户端本地（ConnectionMultiplexer.CheckMessage 的 admin gate 判定）直接拒绝
        // ——2026-09-29 对该实例实测 server.InfoAsync() 抛 RedisCommandException 而
        // ExecuteAsync("INFO") 返回全文；DBSIZE 本就是 per-db 命令，也不需要 IServer/
        // GetEndPoints 的端点猜测，统一走当前 IDatabase 最简。
        var infoErrors = new List<string>();
        // 两条独立命令先发起再各自 await：SE.Redis 只在没有 await 间隔地排队时才自动管线化，
        // 串行写法会把它们拆成两个 RTT（每次监控页加载/刷新都付双倍网络腿）
        var sizeTask = database.ExecuteAsync("DBSIZE");
        var infoTask = database.ExecuteAsync("INFO");
        try
        {
            dto.TotalKeys = (long)await sizeTask;
        }
        catch (Exception ex)
        {
            infoErrors.Add($"DBSIZE：{ex.Message}");
        }

        try
        {
            var rawInfo = await infoTask;
            ApplyInfoText(dto, rawInfo.IsNull ? null : rawInfo.ToString());
        }
        catch (Exception ex)
        {
            infoErrors.Add($"INFO：{ex.Message}");
        }

        if (infoErrors.Count > 0)
        {
            dto.InfoError = string.Join("；", infoErrors);
        }

        return dto;
    }

    /// <summary>
    /// 解析 INFO 命令的文本输出到概览 DTO（raw Execute 通道配套——SE.Redis 的结构化
    /// 解析器 IServer.Info 在该实例上不可用，见 GetInfoAsync 注释）。只取三个白名单
    /// 字段；节头（# Server）不含冒号、keyspace 行（db0:keys=…）与 *_human 诱饵键
    /// 不在白名单，均被自然跳过；CRLF 行尾的 \r 由 Trim 处理。
    /// </summary>
    internal static void ApplyInfoText(CacheMonitorInfoDto dto, string? infoText)
    {
        if (infoText == null)
        {
            return;
        }

        foreach (var line in infoText.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "redis_version":
                    dto.RedisVersion = value;
                    break;
                case "used_memory":
                    dto.UsedMemoryBytes = long.TryParse(value, out var used) ? used : null;
                    break;
                case "maxmemory":
                    dto.MaxMemoryBytes = long.TryParse(value, out var max) ? max : null;
                    break;
            }
        }
    }

    public virtual async Task<CacheKeyListResultDto> GetKeysAsync(string? prefix, long cursor, int maxResultCount)
    {
        var database = await RequireDatabaseAsync();
        maxResultCount = Math.Clamp(maxResultCount, 1, 200);

        // 锚定到 ABP 键空间的扫描模式（信息隔离 + 隔离前缀必含；口径与构造依据见 BuildScanPattern）
        var pattern = BuildScanPattern(prefix, _cacheOptions.KeyPrefix);

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
            // 后置过滤（双保险）：glob 锚定段理论上可被 KeyPrefix 中的元字符破坏，
            // 回显前再过一遍 IsAllowedKey——返回永不越出 ABP 键空间
            keys.AddRange(first.Keys.Where(IsAllowedKey));
        }

        // MATCH 命中稀疏时（冷门前缀）可能要翻完整个键空间才凑满一页——加迭代上限：
        // 到顶即停，返回已凑到的键 + 真实游标（客户端从该游标续扫，语义不重不漏）
        var maxScanIterations = 200;
        while (keys.Count < maxResultCount && scanCursor != 0 && maxScanIterations-- > 0)
        {
            var result = await ScanPageAsync(database, scanCursor, pattern, maxResultCount);
            scanCursor = result.Cursor;
            keys.AddRange(result.Keys.Where(IsAllowedKey));
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

        var dtos = await HydrateKeysAsync(database, page);

        return new CacheKeyListResultDto
        {
            Keys = dtos,
            NextCursor = nextCursor
        };
    }

    /// <summary>
    /// 为本页键补齐元数据（TYPE/TTL/MEMORY USAGE）。每键 3 次往返合到一个 IBatch 里发，
    /// 一批 50 键从 150+ 次 RTT 降为 1 次批往返。Execute 保持同步是有据可查的取舍：
    /// StackExchange.Redis 的 IBatch 只有 void Execute()，没有 ExecuteAsync()（已对照
    /// 2.7.33 与 2.9.x 反编译核实）；Execute 只是把已排队的异步命令冲刷出去，命令结果
    /// 全部由 await 的任务异步消费，不在 I/O 上阻塞线程。
    /// protected virtual 是单测缝：分页协议测试（CacheMonitorScanPaginationTests）以
    /// 脚本化 SCAN 驱动 GetKeysAsync，本方法在彼处短路——真实批处理属 Redis 集成层。
    /// </summary>
    protected virtual async Task<List<CacheKeyDto>> HydrateKeysAsync(IDatabase database, List<string> page)
    {
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

        return dtos;
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
        string? dataField = null;

        switch (type)
        {
            case RedisType.String:
                var value = await database.StringGetAsync(key);
                AppendPreview(builder, value.ToString(), ref truncated.Value);
                break;

            case RedisType.Hash:
                await AppendHashPreviewAsync(database, key, builder, truncated);
                // data 字段原文单独取（写值预填用）：预览是给人看的格式化文本，回写要的是原始值
                dataField = await ReadDataFieldAsync(database, key);
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
            Truncated = truncated.Value,
            DataField = dataField
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

    [Authorize(AbpAdminPermissions.CacheMonitor.Manage)]
    public virtual async Task SetValueAsync(CacheSetValueInput input)
    {
        AssertAbpKey(input.Key);
        if (input.Value.Length > WritableValueLimit)
        {
            throw new UserFriendlyException($"值长度 {input.Value.Length} 超过上限 {WritableValueLimit} 字符——排障工具不承运大对象。");
        }

        var database = await RequireDatabaseAsync();
        var type = await database.KeyTypeAsync(input.Key);
        var now = DateTimeOffset.UtcNow;

        if (type == RedisType.Hash)
        {
            // 写 data 字段；过期元数据按"未提及沿用既有"合并后全量回写（保持 RedisCache 条目格式）
            var (currentAbs, currentSld) = await ReadMetadataAsync(database, input.Key);
            await WriteHashEntryAsync(database, input.Key, input.Value, currentAbs, currentSld,
                input.AbsoluteExpirationSeconds, input.SlidingExpirationSeconds, now);
            return;
        }

        if (type == RedisType.String)
        {
            var (abs, sld) = MergeMetadata(null, null,
                input.AbsoluteExpirationSeconds, input.SlidingExpirationSeconds, now);
            var ttl = ComputeRedisTtl(abs, sld, now);
            AssertFutureTtl(ttl);
            // 两个过期都没给 = 覆盖为永不过期（string 键无元数据可"沿用"，语义就是显式覆盖）
            await database.StringSetAsync(input.Key, input.Value, ttl);
            return;
        }

        throw new UserFriendlyException($"仅支持 hash / string 键的写值（当前类型：{type}）。");
    }

    [Authorize(AbpAdminPermissions.CacheMonitor.Manage)]
    public virtual async Task RefreshAsync(CacheRefreshInput input)
    {
        AssertAbpKey(input.Key);
        var database = await RequireDatabaseAsync();
        var type = await database.KeyTypeAsync(input.Key);
        var now = DateTimeOffset.UtcNow;

        if (type == RedisType.Hash)
        {
            // 不带 data 的 hash 写入 = 只重写过期元数据；两个输入都为 null 时即"按原口径续期"
            // （滑动过期从现在重新起算，语义与 IDistributedCache.RefreshAsync 一致）
            var (currentAbs, currentSld) = await ReadMetadataAsync(database, input.Key);
            await WriteHashEntryAsync(database, input.Key, value: null, currentAbs, currentSld,
                input.AbsoluteExpirationSeconds, input.SlidingExpirationSeconds, now);
            return;
        }

        if (type == RedisType.String)
        {
            if (!input.AbsoluteExpirationSeconds.HasValue && !input.SlidingExpirationSeconds.HasValue)
            {
                throw new UserFriendlyException("string 键没有过期元数据，改期必须显式提供绝对或滑动过期（秒）。");
            }

            var (abs, sld) = MergeMetadata(null, null,
                input.AbsoluteExpirationSeconds, input.SlidingExpirationSeconds, now);
            var ttl = ComputeRedisTtl(abs, sld, now);
            AssertFutureTtl(ttl);
            await database.KeyExpireAsync(input.Key, ttl!.Value);
            return;
        }

        throw new UserFriendlyException($"仅支持 hash / string 键的改期（当前类型：{type}）。");
    }

    /// <summary>
    /// Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache 条目 hash 的字段名（对齐其实现，
    /// 拼错字段名等于静默丢元数据）：absexp=绝对过期（epoch 毫秒，-1=无）；sldexp=滑动过期
    /// （TimeSpan ticks，-1=无）；data=条目值（ABP 缓存为序列化 JSON 信封）。
    /// </summary>
    internal const string AbsoluteExpirationField = "absexp";
    internal const string SlidingExpirationField = "sldexp";
    internal const string DataField = "data";

    /// <summary>写值/预填的单值上限（字符，与值预览同上限）：排障工具不承运大对象，
    /// 且超长值的预填截断后再存回去就是数据损坏。</summary>
    internal const int WritableValueLimit = ContentPreviewLimit;

    /// <summary>hash 的 data 字段原文（写值预填用）。超上限返回 null——半截值存回去就是数据损坏，宁可不给预填。</summary>
    protected virtual async Task<string?> ReadDataFieldAsync(IDatabaseAsync database, string key)
    {
        var value = await database.HashGetAsync(key, DataField);
        var text = value.IsNull ? null : (string?)value;
        return text is { Length: <= WritableValueLimit } ? text : null;
    }

    /// <summary>
    /// hash 条目写入（value=null 时只重写过期元数据 = 改期）。absexp/sldexp 恒全量回写——
    /// RedisCache 的 Get 依赖这两个字段判定条目生死，漏写/写歪等于把活条目判成过期。
    /// TTL 按 RedisCache 同一口径（绝对过期距今与滑动过期取最小）随写更新；data 与元数据
    /// 分两条命令、无事务——排障工具的单写者场景下中间态窗口（元数据先变、值未变）无实际危害，
    /// 不为此引入 Lua/事务复杂度。
    /// </summary>
    protected virtual async Task WriteHashEntryAsync(
        IDatabase database, string key, string? value,
        long? currentAbsoluteMs, TimeSpan? currentSliding,
        long? absoluteSeconds, long? slidingSeconds, DateTimeOffset now)
    {
        var (abs, sld) = MergeMetadata(currentAbsoluteMs, currentSliding, absoluteSeconds, slidingSeconds, now);
        var ttl = ComputeRedisTtl(abs, sld, now);
        AssertFutureTtl(ttl);

        var entries = new List<HashEntry>(3)
        {
            new(AbsoluteExpirationField, (abs ?? -1).ToString(CultureInfo.InvariantCulture)),
            new(SlidingExpirationField, (sld?.Ticks ?? -1).ToString(CultureInfo.InvariantCulture)),
        };
        if (value != null)
        {
            entries.Add(new HashEntry(DataField, value));
        }
        await database.HashSetAsync(key, [.. entries]);
        if (ttl.HasValue)
        {
            await database.KeyExpireAsync(key, ttl.Value);
        }
    }

    /// <summary>读条目的过期元数据（字段缺失 = 未设置；RedisCache 写入时恒写三字段，老条目/外部写入可能缺失）。</summary>
    protected virtual async Task<(long? AbsoluteExpirationUnixMs, TimeSpan? SlidingExpiration)> ReadMetadataAsync(
        IDatabaseAsync database, string key)
    {
        var values = await database.HashGetAsync(key, [AbsoluteExpirationField, SlidingExpirationField]);
        return ParseMetadata(values[0], values[1]);
    }

    /// <summary>解析 RedisCache 元数据字段：absexp（epoch 毫秒）/-1=无；sldexp（TimeSpan ticks）/-1=无。</summary>
    internal static (long? AbsoluteExpirationUnixMs, TimeSpan? SlidingExpiration) ParseMetadata(
        RedisValue absoluteMs, RedisValue slidingTicks)
    {
        long? abs = null;
        if (!absoluteMs.IsNull && long.TryParse(absoluteMs.ToString(), CultureInfo.InvariantCulture, out var a) && a > 0)
        {
            abs = a;
        }

        TimeSpan? sld = null;
        if (!slidingTicks.IsNull && long.TryParse(slidingTicks.ToString(), CultureInfo.InvariantCulture, out var s) && s > 0)
        {
            sld = TimeSpan.FromTicks(s);
        }

        return (abs, sld);
    }

    /// <summary>合并新过期与既有元数据：null 输入沿用既有（写值/改期不改变未提及的口径），有值则覆盖。</summary>
    internal static (long? AbsoluteExpirationUnixMs, TimeSpan? SlidingExpiration) MergeMetadata(
        long? currentAbsoluteMs, TimeSpan? currentSliding,
        long? absoluteSeconds, long? slidingSeconds, DateTimeOffset now)
    {
        var abs = absoluteSeconds.HasValue
            ? now.AddSeconds(absoluteSeconds.Value).ToUnixTimeMilliseconds()
            : currentAbsoluteMs;
        var sld = slidingSeconds.HasValue ? TimeSpan.FromSeconds(slidingSeconds.Value) : currentSliding;
        return (abs, sld);
    }

    /// <summary>
    /// 键 TTL 的 RedisCache 口径（其 Set/Refresh 尾部：过期时间取绝对过期距今与滑动过期的最小值）；
    /// 都未设置 = 永不过期（null）。绝对过期已过时结果为负——调用方须先过 AssertFutureTtl。
    /// </summary>
    internal static TimeSpan? ComputeRedisTtl(long? absoluteMs, TimeSpan? sliding, DateTimeOffset now)
    {
        TimeSpan? absTtl = absoluteMs.HasValue
            ? TimeSpan.FromMilliseconds(absoluteMs.Value - now.ToUnixTimeMilliseconds())
            : null;
        if (absTtl.HasValue && sliding.HasValue)
        {
            return absTtl.Value < sliding.Value ? absTtl : sliding;
        }

        return absTtl ?? sliding;
    }

    /// <summary>
    /// 算出的 TTL 必须在未来：已过期的口径若直接 EXPIRE，负值会把键当场删掉——删除是
    /// 删除功能的显式动作，不归写值/改期静默顺手做。
    /// </summary>
    internal static void AssertFutureTtl(TimeSpan? ttl)
    {
        if (ttl is { } value && value <= TimeSpan.Zero)
        {
            throw new UserFriendlyException(
                $"过期时间已过（按新口径 TTL={value.TotalSeconds:F0} 秒），写值/改期拒绝执行；要立即删除请用删除功能。");
        }
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
    /// 只放行本应用的 ABP 缓存键，防止共享 Redis 上读写/误删其他系统的数据。
    /// 判定依据（rel-10.6.1 DistributedCacheKeyNormalizer 反编译，DistributedCacheKeyShapeTests
    /// 机器钉住）：键恒以结构性标记开头——c:（宿主）或 t:（租户 t:{TenantId},c:...）；
    /// <see cref="AbpDistributedCacheOptions.KeyPrefix"/> 不在键首，插在 k: 段内
    /// （键形如 c:{CacheName},k:{KeyPrefix}{业务key}）。配置了隔离前缀时守卫随之收紧：
    /// 键还必须含 ",k:{KeyPrefix}"——其它 ABP 应用的键（无此前缀或前缀不同）据此拒读拒删。
    /// 隔离边界不得宽于缓存自身的隔离机制（此前前缀只作页面默认过滤词，清空搜索框即可
    /// 越界浏览/删除，属装饰性边界）。
    /// </summary>
    protected virtual void AssertAbpKey(string key)
    {
        if (!IsAllowedKey(key))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Monitoring.CacheMonitorKeyNotAllowed)
                .WithData("KeyPrefix", string.IsNullOrEmpty(_cacheOptions.KeyPrefix)
                    ? "c: / t:"
                    : $"c: / t: 且 k: 段含隔离前缀 {_cacheOptions.KeyPrefix}");
        }
    }

    /// <summary><see cref="AssertAbpKey"/> 的非抛出版（键枚举的后置过滤复用同一判定）。</summary>
    protected virtual bool IsAllowedKey(string key)
    {
        if (!key.StartsWith("c:", StringComparison.Ordinal) &&
            !key.StartsWith("t:", StringComparison.Ordinal))
        {
            return false;
        }

        var keyPrefix = _cacheOptions.KeyPrefix;
        return string.IsNullOrEmpty(keyPrefix) ||
               key.Contains(",k:" + keyPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// 构造键枚举的 SCAN MATCH 模式——锚定到 ABP 键空间，口径与 <see cref="AssertAbpKey"/> 一致：
    /// 键恒以结构性标记开头（c: 宿主 / t: 租户，键形依据见 IsAllowedKey 注释），
    /// 默认锚定 glob 字符类 <c>[ct]:</c> 一次覆盖两种（单模式单游标）；用户词以 c:/t:
    /// 开头时把锚定段收窄到该前缀并剥掉重复段——否则过滤词 <c>c:</c> 会变成
    /// <c>[ct]:*c:*</c>（要求前缀之后再次出现 "c:"），一个键都匹配不到（2026-09-29
    /// 页面实测回归）。隔离前缀（keyPrefix，来自 AbpDistributedCacheOptions）非空时作为
    /// 必含段拼进 glob——扫描天然只命中本应用键，与 IsAllowedKey 的硬边界同口径；用户词
    /// 已含前缀时不重复拼（页面默认过滤词就是 KeyPrefix，重复拼接会要求键中出现两次而漏光）。
    /// 依据（redis.io SCAN 文档）：MATCH 是元素取回之后的过滤，锚定并不减少服务器扫描量，
    /// 收益是信息隔离——共享 Redis 上不把其它系统的键名/元数据回显给持监控查看权限的人。
    /// 用户词原样进入 glob（含元字符也只影响中段、逃不出锚定），枚举结果仍过
    /// <see cref="IsAllowedKey"/> 兜底。
    /// </summary>
    internal static string BuildScanPattern(string? prefix, string? keyPrefix)
    {
        prefix ??= string.Empty;
        string anchor;
        if (prefix.StartsWith("c:", StringComparison.Ordinal))
        {
            anchor = "c:";
            prefix = prefix[2..];
        }
        else if (prefix.StartsWith("t:", StringComparison.Ordinal))
        {
            anchor = "t:";
            prefix = prefix[2..];
        }
        else
        {
            anchor = "[ct]:";
        }

        var containsIsolation = !string.IsNullOrEmpty(keyPrefix) &&
                                prefix.Contains(keyPrefix, StringComparison.Ordinal);
        // 无必含段时与既有口径逐字节一致（含用户词为空时的收尾 **——glob 语义等价于 *，
        // 旧测试按字面钉住）；有必含段时单星拼接，避免空段插值出 ** 改变全部历史期望
        if (string.IsNullOrEmpty(keyPrefix) || containsIsolation)
        {
            return $"{anchor}*{prefix}*";
        }
        return $"{anchor}*{keyPrefix}*{prefix}*";
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
    /// protected virtual 是单测缝：分页协议（溢出缓冲/合成游标/迭代熔断）的测试以脚本化
    /// 应答重写本方法驱动 GetKeysAsync，不依赖真 Redis（见 CacheMonitorScanPaginationTests）。
    /// </summary>
    protected virtual async Task<(long Cursor, List<string> Keys)> ScanPageAsync(
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
