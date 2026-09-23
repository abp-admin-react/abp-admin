using Volo.Abp.MultiTenancy;

namespace AbpAdmin.IpRegions;

/// <summary>
/// IP→归属地 缓存项。负结果（Location=null，未命中库）也缓存，防同一 IP 反复穿透。
/// </summary>
[IgnoreMultiTenancy]
public class IpLocationCacheItem
{
    /// <summary>缓存项 schema 版本。改了字段形状就必须递增。</summary>
    public const int SchemaVersion = 1;

    public string? Location { get; set; }
}
