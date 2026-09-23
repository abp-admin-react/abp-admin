namespace AbpAdmin.IpRegions;

/// <summary>
/// IP 归属地原始查询（离线 xdb 库的薄封装）。实现必须自我兜底：
/// 库未配置/文件缺失/查询异常一律返回 null（功能降级），绝不抛异常传染调用方。
/// </summary>
public interface IIpRegionSearcher
{
    /// <summary>
    /// 查询 IP 的原始归属地串（"国家|区域|省|市|ISP" 格式，"0" 表示缺失段）；未命中返回 null。
    /// </summary>
    string? Search(string ip);
}
