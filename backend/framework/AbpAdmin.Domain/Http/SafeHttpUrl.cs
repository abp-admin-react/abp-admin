using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.Http;

/// <summary>
/// 出站 URL 的 SSRF 防线（Webhook 投递与 HTTP 探活共用）：
/// 目标主机不允许解析到内网/保留地址。IP 字面量直接查网段表；域名做 DNS 解析后
/// 逐个检查解析结果（提交时 + 发送前各查一次，把 DNS rebinding 的利用窗口压缩到
/// 校验与连接之间的毫秒级；彻底根除需要连接层钉住已校验 IP，HttpClient 生态不支持，
/// 不做）。DNS 解析失败按放行处理——解析不了的主机连不上、请求自身以网络错误失败；
/// "守卫查询 NXDOMAIN、连接时切内网记录"的翻转与经典 rebinding 同族（需攻击者持有
/// 权威 DNS + 毫秒级窗口，发送前复查已收窄），归入同一可接受残余，见上方 rebinding 说明。
/// 网段表覆盖 RFC1918 私有段、loopback、链路本地（含云元数据 169.254.169.254）、
/// CGNAT、NAT64 well-known、组播与保留段的 IPv4/IPv6 两侧；IPv4-mapped IPv6
/// （::ffff:a.b.c.d）先归一成 IPv4 再查表，NAT64/十六进制等绕写同被覆盖
/// （十进制/十六进制整数形式的 IPv4 主机会被 IPAddress.TryParse 归一）。
/// 另一层必须配套的防线：两个出站命名 HttpClient 都挂 <see cref="CreateNoRedirectPrimaryHandler"/>
/// 关闭自动重定向——本守卫只校验入口 URL，若跟随 3xx 到内网目标则防线整体失效
/// （webhook 还会把签名头与 payload 重放给重定向目标）。
/// 内网部署确需指向内网目标时，经配置开关显式放行（按消费方各自的键）：
/// 走 <see cref="GetBlockedHostAsync(string, IConfiguration, string)"/>，
/// 开关值非 "true"（含缺失/垃圾值）一律维持拒绝。
/// </summary>
public static class SafeHttpUrl
{
    /// <summary>
    /// 出站命名 HttpClient 统一的主处理器工厂：关闭自动重定向。SSRF 防线只校验
    /// 入口 URL；跟随 3xx 会让服务端替攻击者把请求打到重定向目标（含 https→http
    /// 降级），webhook 的签名头与 payload 也会被重放。接线处（Application 探活客户端、
    /// Webhooks 投递客户端）必须经 ConfigurePrimaryHttpMessageHandler 挂本工厂。
    /// </summary>
    public static HttpClientHandler CreateNoRedirectPrimaryHandler()
        => new() { AllowAutoRedirect = false };

    /// <summary>
    /// 业务侧统一入口：先看消费方的"内网放行"开关（<paramref name="allowIntranetKey"/>，
    /// 显式 true 才放行），再查目标是否命中被禁网段。
    /// 返回 null = 放行；非空 = 违规主机/IP（用于错误消息与日志）。
    /// </summary>
    public static async Task<string?> GetBlockedHostAsync(
        string url, IConfiguration configuration, string allowIntranetKey)
    {
        if (bool.TryParse(configuration[allowIntranetKey], out var allow) && allow)
        {
            return null;
        }

        return await GetBlockedHostAsync(url);
    }

    /// <summary>
    /// 检查 http(s) URL 的目标主机是否命中被禁网段。
    /// 返回 null = 放行；返回非空 = 违规主机/IP（用于错误消息与日志）。
    /// URL 不可解析时返回 null（scheme 合法性由调用方入口校验负责）。
    /// </summary>
    public static async Task<string?> GetBlockedHostAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return await GetBlockedHostAsync(uri);
    }

    /// <summary>同 <see cref="GetBlockedHostAsync(string)"/>，入参为已解析的绝对 http(s) URI。</summary>
    public static async Task<string?> GetBlockedHostAsync(Uri uri)
    {
        // DnsSafeHost = 去端口/去 userinfo/去 IPv6 方括号的裸主机（Uri.Host 对 IPv6 保留方括号）。
        // IPAddress.TryParse 吃下点分与整数形式（http://2130706433/ → 127.0.0.1）的 IP 字面量
        var host = uri.DnsSafeHost;
        if (IPAddress.TryParse(host, out var literal))
        {
            return IsBlockedAddress(literal) ? literal.ToString() : null;
        }

        IPAddress[] resolved;
        try
        {
            resolved = await Dns.GetHostAddressesAsync(host);
        }
        catch (SocketException)
        {
            // 解析失败 = 放行（连不上的主机构不成 SSRF，请求自身会以网络错误失败；
            // NXDOMAIN 翻转残余见类注释——与 rebinding 同族，发送前复查已收窄）
            return null;
        }

        // 域名多 A/AAAA 记录时任一命中即拒绝——攻击者只需一条内网记录即可完成探测
        return resolved.FirstOrDefault(IsBlockedAddress)?.ToString();
    }

    /// <summary>地址是否属于被禁网段（IPv4-mapped IPv6 先归一成 IPv4）。</summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        // 表按 AddressFamily 分流，地址与前缀字节长度必然一致
        var table = address.AddressFamily == AddressFamily.InterNetwork
            ? BlockedV4Networks
            : BlockedV6Networks;

        foreach (var (prefix, prefixLength) in table)
        {
            if (MatchesPrefix(address, prefix, prefixLength))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPrefix(IPAddress address, IPAddress prefix, int prefixLength)
    {
        var addressBytes = address.GetAddressBytes();
        var prefixBytes = prefix.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainderBits = prefixLength % 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != prefixBytes[i])
            {
                return false;
            }
        }

        if (remainderBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainderBits));
            if ((addressBytes[fullBytes] & mask) != (prefixBytes[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>IPv4 被禁网段：本网络/私有/loopback/链路本地/CGNAT/IETF 专用/文档段/基准测试段/组播/保留段。</summary>
    private static readonly (IPAddress Prefix, int Length)[] BlockedV4Networks =
    {
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.0.2.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("198.51.100.0"), 24),
        (IPAddress.Parse("203.0.113.0"), 24),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4),
    };

    /// <summary>IPv6 被禁网段：未指定/loopback/NAT64 well-known/丢弃段/文档段/ULA/链路本地/组播。</summary>
    private static readonly (IPAddress Prefix, int Length)[] BlockedV6Networks =
    {
        (IPAddress.Parse("::"), 128),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("64:ff9b::"), 96),
        (IPAddress.Parse("100::"), 64),
        (IPAddress.Parse("2001:db8::"), 32),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("ff00::"), 8),
    };
}
