using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AbpAdmin.IpRegions;

/// <summary>
/// IpRegionSearcher 双栈真实查询测试（IP2Region.Net Searcher + 真实 xdb 数据文件）。
/// xdb 数据文件随仓库提交（基础数据，见 backend/etc/ip2region/README.md），正常检出即可跑；
/// 找不到文件时（如文件被误删）用例直接返回而非报错——归属地是展示增强，不该让测试基础设施
/// 替文件看门。
/// </summary>
public class IpRegionSearcherFileTests
{
    [Fact]
    public void Ipv4_Public_Should_Resolve()
    {
        var searcher = TryCreate();
        if (searcher == null) return;

        searcher.Search("8.8.8.8").ShouldNotBeNull();
    }

    [Fact]
    public void Ipv6_Public_Should_Resolve()
    {
        var searcher = TryCreate();
        if (searcher == null) return;

        // 240e:56:4000::a4 中国电信北京段：上游 v6 库收录的主流运营商段（手机流量直连 IPv6 的典型形态）
        var location = searcher.Search("240e:56:4000::a4");
        location.ShouldNotBeNull();
        location.ShouldContain("中国");
    }

    [Fact]
    public void Ipv4_Mapped_Ipv6_Should_Route_To_V4_Stack()
    {
        var searcher = TryCreate();
        if (searcher == null) return;

        // ::ffff:8.8.8.8 是 8.8.8.8 的 mapped 形态，两条查询必须得到同一归属地
        var direct = searcher.Search("8.8.8.8");
        var mapped = searcher.Search("::ffff:8.8.8.8");
        mapped.ShouldNotBeNull();
        mapped.ShouldBe(direct);
    }

    [Fact]
    public void Invalid_Ip_Should_Return_Null()
    {
        var searcher = TryCreate();
        if (searcher == null) return;

        searcher.Search("not-an-ip").ShouldBeNull();
        searcher.Search("").ShouldBeNull();
    }

    /// <summary>
    /// 用真实 xdb 构造被测对象；从测试 bin 目录向上回溯定位 backend/etc/ip2region，
    /// 找不到（数据文件未下载）返回 null，用例自跳过。配置传绝对路径，ContentRoot 不参与解析。
    /// </summary>
    private static IIpRegionSearcher? TryCreate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent!)
        {
            var candidate = Path.Combine(dir.FullName, "etc", "ip2region");
            var v4 = Path.Combine(candidate, "ip2region_v4.xdb");
            var v6 = Path.Combine(candidate, "ip2region_v6.xdb");
            if (!File.Exists(v4))
            {
                continue;
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IpRegion:DbPath"] = v4,
                    ["IpRegion:DbPathV6"] = File.Exists(v6) ? v6 : null,
                })
                .Build();

            return new IpRegionSearcher(
                configuration,
                new TestHostEnvironment(),
                NullLogger<IpRegionSearcher>.Instance);
        }

        return null;
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string ApplicationName { get; set; } = "AbpAdmin.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
