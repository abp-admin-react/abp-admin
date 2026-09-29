using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Monitoring;

public interface ICacheMonitorAppService : IApplicationService
{
    /// <summary>缓存后端概览（后端类型、键前缀、Redis 总键数与内存占用）。</summary>
    Task<CacheMonitorInfoDto> GetInfoAsync();

    /// <summary>
    /// 按前缀 SCAN 键（游标分页）。SCAN 是增量式命令，不阻塞 Redis（禁用 KEYS）。
    /// </summary>
    Task<CacheKeyListResultDto> GetKeysAsync(string? prefix, long cursor, int maxResultCount);

    /// <summary>读取单个键的值（文本预览，超过上限截断）。仅允许 ABP 缓存键。</summary>
    Task<CacheValueDto> GetValueAsync(string key);

    /// <summary>删除单个键。仅允许删除 ABP 缓存键（服务端按键结构判定，见实现注释）。</summary>
    Task DeleteKeyAsync(string key);
}

public class CacheMonitorInfoDto
{
    /// <summary>redis | memory。</summary>
    public string Backend { get; set; } = "memory";

    /// <summary>应用隔离前缀（AbpDistributedCacheOptions.KeyPrefix，默认为空）。
    /// rel-10.6 中它插在键的 k: 段内（c:{CacheName},k:{KeyPrefix}{业务key}），不在键首——
    /// 监控页把它作为键浏览的默认过滤词（配置后默认只列出本应用的键）。</summary>
    public string KeyPrefix { get; set; } = default!;

    /// <summary>库内总键数（Redis；memory 后端为 null）。</summary>
    public long? TotalKeys { get; set; }

    /// <summary>Redis 服务器版本。</summary>
    public string? RedisVersion { get; set; }

    /// <summary>Redis 已用内存（字节）。</summary>
    public long? UsedMemoryBytes { get; set; }

    /// <summary>Redis maxmemory（字节，0 = 未限制）。</summary>
    public long? MaxMemoryBytes { get; set; }

    /// <summary>连接失败时的错误信息（Backend=redis 且连不上时非空；此时键浏览/删除均不可用）。</summary>
    public string? ConnectionError { get; set; }

    /// <summary>概览统计命令（DBSIZE/INFO）被服务器拒绝时的原因（连接正常，键浏览/操作不受影响）。
    /// 典型场景：共享实例以非 admin 模式运行时 INFO 会被拒绝（"admin mode is enabled"）。</summary>
    public string? InfoError { get; set; }
}

public class CacheKeyListResultDto
{
    public List<CacheKeyDto> Keys { get; set; } = new();

    /// <summary>下一次 SCAN 的游标；0 表示扫描已到末尾。</summary>
    public long NextCursor { get; set; }
}

public class CacheKeyDto
{
    /// <summary>完整键名。</summary>
    public string Key { get; set; } = default!;

    /// <summary>值类型（string/hash/list/set/zset/stream/none）。</summary>
    public string Type { get; set; } = default!;

    /// <summary>内存占用估算（字节，MEMORY USAGE；服务器不支持时为 null）。</summary>
    public long? SizeBytes { get; set; }

    /// <summary>剩余过期秒数；null = 永不过期。</summary>
    public long? TtlSeconds { get; set; }
}

public class CacheValueDto
{
    public string Key { get; set; } = default!;

    public string Type { get; set; } = default!;

    /// <summary>剩余过期秒数；null = 永不过期。</summary>
    public long? TtlSeconds { get; set; }

    /// <summary>值的文本预览（string 原文；hash 逐字段 field = value；集合逐元素）。</summary>
    public string Content { get; set; } = default!;

    /// <summary>内容超过预览上限被截断。</summary>
    public bool Truncated { get; set; }
}
