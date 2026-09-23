using AbpAdmin.IpRegions;

namespace AbpAdmin.IpRegions;

/// <summary>
/// IP 归属地查询替身：固定映射（公网段命中 / 1.1.1.1 未命中），供解析器与列表填充测试断言。
/// </summary>
public sealed class StubIpRegionSearcher : IIpRegionSearcher
{
    public const string MissIp = "1.1.1.1";

    public string? Search(string ip)
    {
        return ip switch
        {
            MissIp => null,
            _ => "中国|0|浙江省|杭州市|电信",
        };
    }
}
