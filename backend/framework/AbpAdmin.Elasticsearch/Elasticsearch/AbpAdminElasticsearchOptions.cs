using System.Linq;

namespace AbpAdmin.Elasticsearch;

/// <summary>
/// Elasticsearch 接入配置,绑定 "Elasticsearch" 配置节。
/// ABP 框架没有官方 ES 集成包,这里用 Elastic 官方 .NET 客户端自行封装:
/// 运行日志走 Serilog Sink(在 Host 的 Program.cs 按本配置装配,走具体类 ElasticsearchClient——
/// 客户端 9.x 未提供接口抽象),业务/采集数据用本模块注册的 <c>ElasticsearchClient</c> 单例。
/// </summary>
public class AbpAdminElasticsearchOptions
{
    /// <summary>配置节名,绑定/读取统一引用此常量(三处消费方:ES 模块、健康检查注册、Program 日志装配)</summary>
    public const string SectionName = "Elasticsearch";

    /// <summary>数据流默认名,也是 LogDataStream 配置非法时的统一回退值(单一事实来源)</summary>
    public const string DefaultLogDataStream = "logs-abpadmin-default";

    /// <summary>总开关:false 时不注册客户端、不装配日志 Sink、不注册健康检查</summary>
    public bool IsEnabled { get; set; }

    public string Url { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>运行日志写入的数据流名,三段式 {type}-{dataset}-{namespace},dataset 段可含 '-'(如 logs-my-app-default)</summary>
    public string LogDataStream { get; set; } = DefaultLogDataStream;

    /// <summary>
    /// 配置是否足以启用(开关 + 地址完整)。启用谓词单头化:
    /// 模块注册/健康检查注册/日志 sink 装配统一以此判定,避免多处复制漂移。
    /// </summary>
    public bool IsUsable => IsEnabled && !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// 解析 <see cref="LogDataStream"/>(三段式,dataset 段可含 '-')。
    /// 不合规格式(不足 3 段/存在空段)返回 null——调用方必须回退默认值并告警,
    /// 禁止静默改写配置(叠加 sink 的 Silent 自举会变成日志无声丢失)。
    /// </summary>
    public static (string Type, string DataSet, string Namespace)? ParseLogDataStream(string? logDataStream)
    {
        if (string.IsNullOrWhiteSpace(logDataStream))
        {
            return null;
        }

        var parts = logDataStream.Split('-');
        if (parts.Length < 3 || parts.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        return (parts[0], string.Join('-', parts[1..^1]), parts[^1]);
    }
}
