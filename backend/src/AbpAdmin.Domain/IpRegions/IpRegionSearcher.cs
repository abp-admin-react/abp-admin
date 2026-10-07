using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using IP2Region.Net.Abstractions;
using IP2Region.Net.XDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.IpRegions;

/// <summary>
/// IP2Region 离线库封装（对标 ruoyi 的 yudao-spring-boot-starter-biz-ip）。
/// 双栈：IpRegion:DbPath（IPv4）+ IpRegion:DbPathV6（IPv6），按查询 IP 的 AddressFamily 分流；
/// IPv4-mapped IPv6（::ffff:a.b.c.d，手机经 NAT64/DNS64 访问时日志里的常见形态）归 IPv4 库。
/// 相对路径按应用 ContentRoot 解析。两库独立加载、独立降级：单栈未配置/缺失/损坏只关闭该栈
/// （该栈查询返回 null），另一栈照常工作；两栈全缺时功能关闭。
/// CachePolicy.Content：xdb 全量驻留内存（v4 约 11MB、v6 约 37MB），加载后查询线程安全且纳秒级。
/// 启动日志与状态的对应：单栈未配置 → Info；单栈文件缺失/加载失败 → Warning（含路径）；
/// 两栈全缺 → Info「功能关闭」。任何状态都不阻塞应用启动。
/// </summary>
public class IpRegionSearcher : IIpRegionSearcher, ISingletonDependency
{
    private readonly ISearcher? _searcherV4;
    private readonly ISearcher? _searcherV6;

    public IpRegionSearcher(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<IpRegionSearcher> logger)
    {
        _searcherV4 = TryCreateSearcher(
            configuration["IpRegion:DbPath"], "IPv4", hostEnvironment, logger);
        _searcherV6 = TryCreateSearcher(
            configuration["IpRegion:DbPathV6"], "IPv6", hostEnvironment, logger);

        if (_searcherV4 == null && _searcherV6 == null)
        {
            logger.LogInformation("IPv4/IPv6 归属地库均未启用，IP 归属地功能关闭。");
        }
    }

    public string? Search(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            // 非法串：与「该栈未启用」同路径返回 null（IIpRegionSearcher 契约）
            return null;
        }

        // mapped 形态（::ffff:a.b.c.d）先归一成 IPv4。归一化、分流、取查询串必须
        // 同在这一处完成：Searcher 内部按传入串自行 Parse 取字节，若把未归一的
        // mapped 串原样传下去，会被当成 16 字节 IPv6 查 v4 库而落空（返回空串）。
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var searcher = address.AddressFamily == AddressFamily.InterNetwork
            ? _searcherV4
            : _searcherV6;
        if (searcher == null)
        {
            // 该栈未启用：返回 null 而不是换栈，调用方（IpLocationResolver）按未命中处理
            return null;
        }

        try
        {
            return searcher.Search(address.ToString());
        }
        catch
        {
            // 单次查询失败（数据文件损坏的运行期表现等）不影响整体功能
            return null;
        }
    }

    /// <summary>
    /// 加载单栈 xdb；路径未配置/文件缺失/损坏一律返回 null（该栈降级），不阻塞应用启动。
    /// </summary>
    private static ISearcher? TryCreateSearcher(
        string? dbPath, string stackName, IHostEnvironment hostEnvironment, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            logger.LogInformation(
                "未配置 IpRegion:{Stack} 库路径，{Stack} 归属地方向未启用。", stackName, stackName);
            return null;
        }

        try
        {
            if (!Path.IsPathRooted(dbPath))
            {
                dbPath = Path.Combine(hostEnvironment.ContentRootPath, dbPath);
            }

            if (!File.Exists(dbPath))
            {
                logger.LogWarning(
                    "IP 归属地库文件不存在：{DbPath}，{Stack} 方向未启用。", dbPath, stackName);
                return null;
            }

            return new Searcher(CachePolicy.Content, dbPath);
        }
        catch (Exception ex)
        {
            // xdb 损坏等加载失败：该栈降级为未启用，不影响应用启动与另一栈
            logger.LogWarning(ex, "IP 归属地库加载失败：{DbPath}，{Stack} 方向未启用。", dbPath, stackName);
            return null;
        }
    }
}
