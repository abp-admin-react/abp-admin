namespace AbpAdmin.RateLimiting;

/// <summary>
/// 当方法参数类型实现此接口时，通过 <see cref="GetPartitionParameter"/> 获取限流分区参数。
/// 优先级低于 <see cref="RateLimitingParameterAttribute"/>。
/// </summary>
public interface IHasOperationRateLimitingParameter
{
    string? GetPartitionParameter();
}
