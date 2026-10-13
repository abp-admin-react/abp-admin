using Volo.Abp.MultiTenancy;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 解析函数的 ID→名称 缓存项（共享给全部内置函数，靠 key 前缀区分函数）。
/// 实体 Guid 全局唯一、名称与查看方租户无关 → 跨租户共享，避免同一 ID 在 host/各租户各存一份。
/// </summary>
[IgnoreMultiTenancy]
public class OperationLogNameCacheItem
{
    /// <summary>缓存项 schema 版本。改了字段形状就必须递增（见 DataScopeCacheItem 同款注释）。</summary>
    public const int SchemaVersion = 1;

    /// <summary>未命中/查询失败时也允许为 null 以外的兜底文案（「未知(前8位)」）。</summary>
    public string? Name { get; set; }
}
