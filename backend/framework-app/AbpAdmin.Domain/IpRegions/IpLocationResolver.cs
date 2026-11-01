using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.IpRegions;

public interface IIpLocationResolver
{
    /// <summary>
    /// 解析单个 IP 的归属地展示文本（如「中国 浙江省 杭州市 电信」）。
    /// 内网/回环地址返回「内网IP」；无法解析返回 null。
    /// </summary>
    ValueTask<string?> ResolveAsync(string? ipAddress);

    /// <summary>
    /// 批量解析（列表页按当页去重 IP 解析）：返回 IP → 归属地（可能为 null）的字典，
    /// 未出现的 IP 不在字典里。内部走批量缓存读写（Redis 一次往返）。
    /// </summary>
    Task<Dictionary<string, string?>> ResolveManyAsync(IEnumerable<string?> ipAddresses);
}

/// <summary>
/// IP 归属地解析器：离线库查询 + 分布式缓存（7 天，IP 归属地事实不变）。
/// 归属地是纯展示增强：缓存（如 Redis）故障时降级为直查/跳过，绝不把日志列表页打挂。
/// </summary>
public class IpLocationResolver : IIpLocationResolver, ITransientDependency
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    private readonly IIpRegionSearcher _searcher;
    private readonly IDistributedCache<IpLocationCacheItem> _cache;

    public IpLocationResolver(
        IIpRegionSearcher searcher,
        IDistributedCache<IpLocationCacheItem> cache)
    {
        _searcher = searcher;
        _cache = cache;
    }

    public virtual async ValueTask<string?> ResolveAsync(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out _))
        {
            return null;
        }

        // 内网/回环不打库也不进缓存：结果恒定，判断开销可忽略
        if (IsPrivateAddress(ipAddress))
        {
            return "内网IP";
        }

        var cached = await TryGetCacheAsync(ipAddress);
        if (cached != null)
        {
            return cached.Location;
        }

        var location = FormatRegion(_searcher.Search(ipAddress));
        // 负结果同样缓存：未分配/海外段 IP 不反复穿透
        await TrySetCacheAsync(ipAddress, new IpLocationCacheItem { Location = location });

        return location;
    }

    public virtual async Task<Dictionary<string, string?>> ResolveManyAsync(IEnumerable<string?> ipAddresses)
    {
        var map = new Dictionary<string, string?>();
        var pending = new List<string>();
        foreach (var ip in ipAddresses)
        {
            if (string.IsNullOrWhiteSpace(ip) || map.ContainsKey(ip))
            {
                continue;
            }

            // 内网/回环短路：不进缓存批次
            if (IsPrivateAddress(ip))
            {
                map[ip] = "内网IP";
                continue;
            }

            map[ip] = null; // 占位保证去重
            pending.Add(ip);
        }

        if (pending.Count == 0)
        {
            return map;
        }

        // 批量读（Redis MGET 一次往返）；故障降级视作全未命中
        var cachedPairs = await TryGetManyCacheAsync(pending);
        var cachedMap = cachedPairs.ToDictionary(x => x.Key, x => x.Value);
        var misses = new List<string>();
        foreach (var ip in pending)
        {
            if (cachedMap.TryGetValue(ip, out var item) && item != null)
            {
                map[ip] = item.Location;
            }
            else
            {
                misses.Add(ip);
            }
        }

        if (misses.Count == 0)
        {
            return map;
        }

        // 未命中的逐个查离线库（纯内存，无 IO），结果批量写回
        var toSet = new List<KeyValuePair<string, IpLocationCacheItem>>();
        foreach (var ip in misses)
        {
            var location = FormatRegion(_searcher.Search(ip));
            map[ip] = location;
            // 负结果同样缓存，防穿透
            toSet.Add(new(ip, new IpLocationCacheItem { Location = location }));
        }

        await TrySetManyCacheAsync(toSet);
        return map;
    }

    /// <summary>"中国|0|浙江省|杭州市|电信" → "中国 浙江省 杭州市 电信"（"0"/空段丢弃）。</summary>
    private static string? FormatRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        var parts = region
            .Split('|')
            .Select(x => string.IsNullOrWhiteSpace(x) || x.Trim() == "0" ? null : x.Trim())
            .Where(x => x != null)
            .ToList();

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>内网/回环/链路本地判断——这些地址查归属地没有意义。</summary>
    private static bool IsPrivateAddress(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10                                                    // 10.0.0.0/8
                   || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)          // 172.16.0.0/12
                   || (bytes[0] == 192 && bytes[1] == 168)                           // 192.168.0.0/16
                   || (bytes[0] == 169 && bytes[1] == 254);                          // 169.254.0.0/16 链路本地
        }

        // IPv6：fe80::/10 链路本地、fc00::/7 唯一本地
        return (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
               || (bytes[0] & 0xfe) == 0xfc;
    }

    // —— 缓存访问的故障防线：缓存是可选加速，Redis 断连等故障一律降级，不打挂调用方（日志列表主链路） ——

    private async Task<IpLocationCacheItem?> TryGetCacheAsync(string key)
    {
        try
        {
            return await _cache.GetAsync(key);
        }
        catch
        {
            return null;
        }
    }

    private async Task TrySetCacheAsync(string key, IpLocationCacheItem item)
    {
        try
        {
            await _cache.SetAsync(key, item, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
        }
        catch
        {
            // 跳过写缓存
        }
    }

    private async Task<KeyValuePair<string, IpLocationCacheItem?>[]> TryGetManyCacheAsync(List<string> keys)
    {
        try
        {
            return await _cache.GetManyAsync(keys);
        }
        catch
        {
            return [];
        }
    }

    private async Task TrySetManyCacheAsync(List<KeyValuePair<string, IpLocationCacheItem>> items)
    {
        try
        {
            await _cache.SetManyAsync(items, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
        }
        catch
        {
            // 跳过写缓存
        }
    }
}
