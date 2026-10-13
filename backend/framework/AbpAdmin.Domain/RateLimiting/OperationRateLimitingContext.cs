using System.Collections.Generic;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流上下文。
/// </summary>
public class OperationRateLimitingContext
{
    /// <summary>
    /// 分区参数值（原样使用，不做规范化）。
    /// 警告：不要用任何密钥、令牌、密码作为分区值，它们会以明文出现在
    /// 分布式缓存（如 Redis）的 key 里，且可能出现在慢查询日志与监控面板中。
    /// </summary>
    public string? Parameter { get; set; }

    /// <summary>
    /// 附加属性。每一项都会被复制进限流异常的 Data 字典并序列化返回给客户端。
    /// 警告：不要往这里放密钥或任何敏感信息。
    /// </summary>
    public Dictionary<string, object?> ExtraProperties { get; set; } = new();
}
