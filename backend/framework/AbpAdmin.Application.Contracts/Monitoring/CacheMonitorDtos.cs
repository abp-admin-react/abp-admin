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

    /// <summary>写值：hash 写 data 字段并按需重写过期元数据，string 覆盖原文。仅允许 ABP 缓存键（Manage 权限）。</summary>
    Task SetValueAsync(CacheSetValueInput input);

    /// <summary>改期：重写 absexp/sldexp 元数据并按 RedisCache 同口径（两者取最小）重设 TTL，滑动过期从现在重新起算。仅允许 ABP 缓存键（Manage 权限）。</summary>
    Task RefreshAsync(CacheRefreshInput input);
}

public class CacheMonitorInfoDto
{
    /// <summary>redis | memory。</summary>
    public string Backend { get; set; } = "memory";

    /// <summary>应用隔离前缀（AbpDistributedCacheOptions.KeyPrefix，默认为空）。
    /// rel-10.6.1 中它插在键的 k: 段内（c:{CacheName},k:{KeyPrefix}{业务key}），不在键首——
    /// 配置后是监控的硬边界（键浏览/读值/删除只限含此前缀的本应用键），同时仍是
    /// 键浏览的默认过滤词。</summary>
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

    /// <summary>
    /// hash 键 data 字段的原文（Microsoft RedisCache 条目格式，写值预填用；string 键用 Content 预填）。
    /// ABP 缓存条目的 data 是序列化 JSON 信封——写回必须保持可反序列化。
    /// 键过大或值超过预览上限时为 null（半截值存回去就是数据损坏，宁可不给预填）。
    /// </summary>
    public string? DataField { get; set; }
}

/// <summary>写值输入（对标 abp-next-admin CachingManagement 的 Set：hash 写 data 字段，string 覆盖原文）。</summary>
public class CacheSetValueInput
{
    /// <summary>完整键名（与键浏览回显一致；服务端按 ABP 键结构 + 隔离前缀判定）。</summary>
    public string Key { get; set; } = default!;

    /// <summary>写入的值原文。hash 键写 data 字段（保持 JSON 信封可反序列化）；string 键覆盖整键。</summary>
    public string Value { get; set; } = default!;

    /// <summary>绝对过期（从现在起算的秒数）；null = 沿用键既有的 absexp 元数据（无则不过期）。</summary>
    public long? AbsoluteExpirationSeconds { get; set; }

    /// <summary>滑动过期（秒数）；null = 沿用既有的 sldexp 元数据。</summary>
    public long? SlidingExpirationSeconds { get; set; }
}

/// <summary>改期输入（对标 abp-next-admin CachingManagement 的 Refresh：重写过期元数据并按同口径重设 TTL）。</summary>
public class CacheRefreshInput
{
    /// <summary>完整键名。</summary>
    public string Key { get; set; } = default!;

    /// <summary>绝对过期（从现在起算的秒数）；null = 沿用既有 absexp（两个都为 null 时 hash 键=按原元数据续期，string 键报错）。</summary>
    public long? AbsoluteExpirationSeconds { get; set; }

    /// <summary>滑动过期（秒数）；null = 沿用既有 sldexp。</summary>
    public long? SlidingExpirationSeconds { get; set; }
}
