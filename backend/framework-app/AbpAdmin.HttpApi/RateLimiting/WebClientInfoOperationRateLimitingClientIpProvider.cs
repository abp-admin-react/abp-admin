using AbpAdmin.RateLimiting;
using Volo.Abp.AspNetCore.WebClientInfo;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 基于 ABP IWebClientInfoProvider 的客户端 IP 提供者实现。
/// 注意：反向代理后需确保 ForwardedHeaders 中间件已启用，否则拿到的是代理 IP。
/// </summary>
public class WebClientInfoOperationRateLimitingClientIpProvider : IOperationRateLimitingClientIpProvider, ITransientDependency
{
    private readonly IWebClientInfoProvider _webClientInfoProvider;

    public WebClientInfoOperationRateLimitingClientIpProvider(IWebClientInfoProvider webClientInfoProvider)
    {
        _webClientInfoProvider = webClientInfoProvider;
    }

    public string? GetClientIpAddress()
    {
        return _webClientInfoProvider.ClientIpAddress;
    }
}
