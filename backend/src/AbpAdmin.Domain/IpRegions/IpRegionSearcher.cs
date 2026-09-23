using System;
using System.IO;
using IP2Region.Net.Abstractions;
using IP2Region.Net.XDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.IpRegions;

/// <summary>
/// IP2Region 离线库封装（对标 ruoyi 的 yudao-spring-boot-starter-biz-ip）。
/// xdb 路径来自配置 IpRegion:DbPath（相对路径按 ContentRoot 解析）；
/// 未配置或文件缺失时整体禁用（所有查询返回 null），不阻塞应用启动。
/// CachePolicy.Content：xdb 全量驻留内存（约 11MB），加载后查询线程安全且纳秒级。
/// </summary>
public class IpRegionSearcher : IIpRegionSearcher, ISingletonDependency
{
    private readonly ISearcher? _searcher;
    private readonly ILogger<IpRegionSearcher> _logger;

    public IpRegionSearcher(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<IpRegionSearcher> logger)
    {
        _logger = logger;

        var dbPath = configuration["IpRegion:DbPath"];
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            logger.LogInformation("未配置 IpRegion:DbPath，IP 归属地功能未启用。");
            return;
        }

        try
        {
            if (!Path.IsPathRooted(dbPath))
            {
                dbPath = Path.Combine(hostEnvironment.ContentRootPath, dbPath);
            }

            if (!File.Exists(dbPath))
            {
                logger.LogWarning("IP 归属地库文件不存在：{DbPath}，功能未启用。", dbPath);
                return;
            }

            _searcher = new Searcher(CachePolicy.Content, dbPath);
        }
        catch (Exception ex)
        {
            // xdb 损坏等加载失败：降级为未启用，不影响应用启动
            logger.LogWarning(ex, "IP 归属地库加载失败：{DbPath}，功能未启用。", dbPath);
        }
    }

    public string? Search(string ip)
    {
        if (_searcher == null)
        {
            return null;
        }

        try
        {
            return _searcher.Search(ip);
        }
        catch
        {
            // 单次查询失败（非法 IP 等）不影响整体功能
            return null;
        }
    }
}
