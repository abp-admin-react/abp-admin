namespace AbpAdmin.RateLimiting;

/// <summary>
/// 客户端 IP 提供者抽象。Domain 层不引用 AspNetCore，由上层（HttpApi/Host）实现并注册。
/// </summary>
public interface IOperationRateLimitingClientIpProvider
{
    /// <summary>获取当前客户端 IP 地址。无法获取时返回 null。</summary>
    string? GetClientIpAddress();
}
