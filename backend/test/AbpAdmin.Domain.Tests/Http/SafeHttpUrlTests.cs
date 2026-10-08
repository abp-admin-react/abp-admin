using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AbpAdmin.Http;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AbpAdmin.Http;

/// <summary>
/// SSRF 防线（SafeHttpUrl）的纯单测：网段表覆盖面（含段边界与 mapped/NAT64 绕写归一）、
/// URL 解析路径（userinfo/整数形式主机）、DNS 路径（localhost 解析 loopback 必拦、
/// NXDOMAIN 放行——解析不了的主机构不成 SSRF）、放行开关重载（显式 true 才放行）、
/// 出站客户端统一关重定向工厂。
/// </summary>
public class SafeHttpUrlTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.0.1")]          // loopback /8 整段
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]       // 172.16/12 的两端
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]      // 云元数据
    [InlineData("100.64.0.1")]           // CGNAT
    [InlineData("100.127.255.254")]      // CGNAT /10 段尾
    [InlineData("0.0.0.3")]
    [InlineData("198.18.5.5")]           // 基准测试段 /15
    [InlineData("224.0.0.1")]            // 组播
    [InlineData("239.1.1.1")]
    [InlineData("240.0.0.1")]            // 保留段
    [InlineData("255.255.255.255")]
    public void IsBlockedAddress_blocks_reserved_ipv4(string ip)
        => SafeHttpUrl.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]       // 172.16/12 段外下边界
    [InlineData("172.32.0.1")]           // 段外上边界
    [InlineData("100.63.255.255")]       // CGNAT 段外
    [InlineData("100.128.0.1")]
    [InlineData("192.167.1.1")]          // 192.168/16 段外
    [InlineData("198.17.0.1")]           // 198.18/15 段外
    [InlineData("223.255.255.255")]      // 组播段外
    public void IsBlockedAddress_allows_public_ipv4(string ip)
        => SafeHttpUrl.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeFalse();

    [Theory]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]    // fc00::/7 两半段（fd = 本地分配位）
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:127.0.0.1")]     // IPv4-mapped 绕写 → 归一成 v4 拦截
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")]   // NAT64 well-known 段内嵌 169.254.169.254
    public void IsBlockedAddress_blocks_reserved_ipv6(string ip)
        => SafeHttpUrl.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeTrue();

    [Theory]
    [InlineData("fec0::1")]                                 // fe80::/10 段外上边界（该段覆盖 fe80~febf）
    [InlineData("fb00::1")]                                 // fc00::/7 段外下边界（该段覆盖 fc00~fdff）
    [InlineData("64:ff9b:0:1::1")]                          // NAT64 /96 段外（前缀长度写错 96→64 会误吞本地址）
    [InlineData("2001:db7::1")]                             // 2001:db8::/32 文档段外
    public void IsBlockedAddress_allows_public_ipv6_outside_edges(string ip)
        => SafeHttpUrl.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeFalse();

    [Theory]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2001:4860:4860::8888")]
    public void IsBlockedAddress_allows_public_ipv6(string ip)
        => SafeHttpUrl.IsBlockedAddress(IPAddress.Parse(ip)).ShouldBeFalse();

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/", "169.254.169.254")]
    [InlineData("http://10.0.0.5:8080/hook", "10.0.0.5")]
    [InlineData("http://[::1]/hook", "::1")]
    [InlineData("http://user:pass@192.168.1.10/hook", "192.168.1.10")] // userinfo 不影响 Host 判定
    [InlineData("http://2130706433/hook", "127.0.0.1")]                // 整数形式 IPv4 字面量被 TryParse 归一成 loopback
    public async Task GetBlockedHostAsync_blocks_ip_literal_urls(string url, string expectedHost)
        => (await SafeHttpUrl.GetBlockedHostAsync(url)).ShouldBe(expectedHost);

    [Theory]
    [InlineData("http://8.8.8.8/dns-query")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    public async Task GetBlockedHostAsync_allows_public_ip_literals(string url)
        => (await SafeHttpUrl.GetBlockedHostAsync(url)).ShouldBeNull();

    [Fact]
    public async Task GetBlockedHostAsync_blocks_loopback_via_dns()
        // localhost 由 hosts 文件解析到 loopback——域名路径（DNS 解析后校验）的确定性锚点
        => (await SafeHttpUrl.GetBlockedHostAsync("http://localhost/hook")).ShouldNotBeNull();

    [Fact]
    public async Task GetBlockedHostAsync_allows_unresolvable_host()
        // .invalid 保留 TLD 保证 NXDOMAIN。fail-open：解析失败放行（连不上的主机构不成
        // SSRF，请求自身以网络错误失败）；NXDOMAIN 翻转与 rebinding 同族（攻击者权威
        // DNS + 毫秒级窗口，发送前复查收窄），归入同一可接受残余——见 SafeHttpUrl 类注释
        => (await SafeHttpUrl.GetBlockedHostAsync("http://no-such-host.invalid/hook")).ShouldBeNull();

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://8.8.8.8/file")]
    public async Task GetBlockedHostAsync_returns_null_for_non_http_urls(string url)
        // scheme 合法性由调用方入口校验负责；这里只负责"主机是否内网"
        => (await SafeHttpUrl.GetBlockedHostAsync(url)).ShouldBeNull();

    /// <summary>门控重载的开关读数（显式 "true" 才放行；缺失/垃圾值一律拒绝）。</summary>
    private static IConfiguration Config(params (string Key, string? Value)[] entries)
        => new ConfigurationBuilder().AddInMemoryCollection(
            entries.ToDictionary(e => e.Key, e => (string?)e.Value)).Build();

    [Fact]
    public async Task Gate_overload_allows_intranet_only_when_switch_is_true()
    {
        const string key = "Test:AllowIntranetTargets";
        const string url = "http://10.0.0.5/hook";

        // 默认（开关缺失）= 拒绝
        (await SafeHttpUrl.GetBlockedHostAsync(url, Config(), key)).ShouldNotBeNull();
        // 垃圾值 = 拒绝（bool.TryParse 失败按未放行处理）
        (await SafeHttpUrl.GetBlockedHostAsync(url, Config((key, "yes")), key)).ShouldNotBeNull();
        // 显式 false = 拒绝；显式 true = 放行（内网部署的文档化出口）
        (await SafeHttpUrl.GetBlockedHostAsync(url, Config((key, "false")), key)).ShouldNotBeNull();
        (await SafeHttpUrl.GetBlockedHostAsync(url, Config((key, "true")), key)).ShouldBeNull();
    }

    [Fact]
    public void NoRedirect_factory_disables_auto_redirect()
        // 两个出站命名客户端（探活/投递）的 SSRF 配套防线：守卫只校验入口 URL，
        // 跟随 3xx 会绕过守卫打到重定向目标（webhook 还会重放签名头与 payload）
        => SafeHttpUrl.CreateNoRedirectPrimaryHandler().AllowAutoRedirect.ShouldBeFalse();
}
