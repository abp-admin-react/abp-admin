using Volo.Abp.DependencyInjection;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 测试用客户端 IP 提供者。Domain.Tests 不引用 AspNetCore，用固定 IP 代替。
/// 可通过静态属性 CurrentIp 在测试中切换 IP。
/// </summary>
[Dependency(ReplaceServices = true)]
public class FakeOperationRateLimitingClientIpProvider : IOperationRateLimitingClientIpProvider, ISingletonDependency
{
    /// <summary>当前返回的 IP 地址。测试中可修改。</summary>
    public static string? CurrentIp { get; set; } = "10.0.0.1";

    public string? GetClientIpAddress() => CurrentIp;
}
