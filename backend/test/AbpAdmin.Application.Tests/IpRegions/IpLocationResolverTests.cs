using System;
using System.Threading.Tasks;
using AbpAdmin.IpRegions;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.IpRegions;

/* IP 归属地解析契约测试：格式化、内网短路、未命中、批量去重。
 * 查询侧用 StubIpRegionSearcher 固定映射（AbpAdminApplicationTestModule 注册）。
 */
public abstract class IpLocationResolverTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IIpLocationResolver _resolver;

    protected IpLocationResolverTests()
    {
        _resolver = GetRequiredService<IIpLocationResolver>();
    }

    [Fact]
    public async Task Public_Ip_Should_Be_Formatted()
    {
        // "中国|0|浙江省|杭州市|电信" → "0" 段丢弃、空格连接
        (await _resolver.ResolveAsync("112.17.10.23"))
            .ShouldBe("中国 浙江省 杭州市 电信");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:10.1.2.3")] // IPv4-mapped：双栈 socket 的典型形态，先映射回 IPv4 再判定
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    public async Task Private_And_Loopback_Should_Short_Circuit(string ip)
    {
        (await _resolver.ResolveAsync(ip)).ShouldBe("内网IP");
    }

    [Fact]
    public async Task Miss_And_Empty_Should_Return_Null()
    {
        (await _resolver.ResolveAsync(StubIpRegionSearcher.MissIp)).ShouldBeNull();
        (await _resolver.ResolveAsync(null)).ShouldBeNull();
        (await _resolver.ResolveAsync("")).ShouldBeNull();
        (await _resolver.ResolveAsync("not-an-ip")).ShouldBeNull();
    }

    [Fact]
    public async Task ResolveMany_Should_Deduplicate_And_Map()
    {
        var map = await _resolver.ResolveManyAsync(
        [
            "112.17.10.23",
            "112.17.10.23", // 重复只解析一次
            "127.0.0.1",
            null,
            StubIpRegionSearcher.MissIp,
        ]);

        map.Count.ShouldBe(3);
        map["112.17.10.23"].ShouldBe("中国 浙江省 杭州市 电信");
        map["127.0.0.1"].ShouldBe("内网IP");
        map[StubIpRegionSearcher.MissIp].ShouldBeNull();
    }
}
