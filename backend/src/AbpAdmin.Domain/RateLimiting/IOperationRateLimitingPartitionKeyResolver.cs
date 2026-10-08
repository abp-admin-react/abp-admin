using System.Threading.Tasks;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 分区键解析器。根据限流上下文解析出分区键（决定"哪些请求共用一个计数器"）。
/// </summary>
public interface IOperationRateLimitingPartitionKeyResolver
{
    /// <summary>
    /// 解析分区键。返回 null 表示无法解析——由 Checker 回退 ClientIp 分区
    /// （连 IP 都拿不到才落 "unknown"），不会使用全局共享计数。
    /// 返回值长度由 Checker 统一钳制（自定义解析器无需自行截断，但内置解析器已各自先截）。
    /// </summary>
    Task<string?> ResolveAsync(OperationRateLimitingContext context);
}
