using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 分区键解析器。根据限流上下文解析出分区键（决定"哪些请求共用一个计数器"）。
/// </summary>
public interface IOperationRateLimitingPartitionKeyResolver
{
    /// <summary>
    /// 解析分区键。返回 null 表示无法解析（将退化为全局共享计数）。
    /// </summary>
    Task<string?> ResolveAsync(OperationRateLimitingContext context);
}
