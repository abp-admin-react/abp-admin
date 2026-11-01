namespace AbpAdmin.RateLimiting;

/// <summary>
/// 限流策略提供者。默认实现从 AbpAdminOperationRateLimitingOptions 读。
/// 可实现此接口从数据库或外部配置加载策略（本轮不做数据库实现，但接口留出来）。
/// </summary>
public interface IOperationRateLimitingPolicyProvider
{
    /// <summary>
    /// 按名称获取策略。策略不存在时返回 null。
    /// </summary>
    OperationRateLimitingPolicy? GetPolicy(string policyName);
}
