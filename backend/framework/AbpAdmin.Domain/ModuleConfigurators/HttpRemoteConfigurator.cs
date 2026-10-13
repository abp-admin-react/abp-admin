using AbpAdmin.Captcha;
using HttpAgent;
using HttpAgent.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 出站 HTTP 统一客户端（HttpAgent，独立包、非 Furion 本体）基础设施配置。
/// 底层仍是 IHttpClientFactory：声明式接口经 [HttpClientName] 绑定各消费方注册的命名 HttpClient，
/// 超时 / SSL / BaseAddress 等 Handler 与客户端定制沿用原注册点不动
/// （如 AbpAdminApplicationModule 的 AuthServerTokenExchange）。
/// 本配置器负责 HttpRemote 基础设施与 Domain 层消费方（Turnstile）的声明式注册及其命名 HttpClient；
/// Application 层的 IConnectTokenApi 在 AbpAdminApplicationModule 随其命名客户端就近注册（注册跟随消费方）。
/// </summary>
internal static class HttpRemoteConfigurator
{
    public static void ConfigureOutboundHttpRemote(this IServiceCollection services)
    {
        var configuration = services.GetConfiguration();

        // T4.4 Turnstile 校验走命名 HttpClient（开关关闭时不会发请求）；
        // 注册自 CachingAndTenancyConfigurator 迁入（round4 审查 D4：绑定与注册同处，好找）。
        services.AddHttpClient(TurnstileHttpClients.SiteVerify);

        // Profiler：HttpRemote:Profiler=true 时【全部】出站客户端（Turnstile / connect/token 回环 /
        // webhook 投递 / 短信 requester 等，含后续新增消费方）控制台直出完整报文（默认关——报文含密钥字段）。
        // ConfigureHttpClientDefaults 对所有命名/默认客户端统一生效，新消费方零接线；
        // 该调用只改 HttpClient 管道，与各客户端注册的先后顺序无关。
        services.ConfigureHttpClientDefaults(clientBuilder =>
            clientBuilder.AddProfilerDelegatingHandler(() => configuration.GetValue("HttpRemote:Profiler", false)));

        services.AddHttpRemote(builder =>
        {
            // 容错转换器：5xx 非 JSON 体不炸穿，Result 落 null 由验证器按非成功处理（fail-closed）
            builder.AddHttpDeclarative<ITurnstileSiteVerifyApi>();
            builder.AddHttpContentConverters(() => new IHttpContentConverter[]
            {
                new HttpRemote.TolerantJsonContentConverter<TurnstileVerifyResponse>()
            });
        });
    }
}
