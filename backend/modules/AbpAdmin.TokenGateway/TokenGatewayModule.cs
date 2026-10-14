using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;

namespace AbpAdmin.TokenGateway;

// HarnessBot 路线 C 外层：用户 Key、代币钱包、充值单、OpenAI 兼容反代。
// MVP 账本在内存（进程重启清零），落库是下一批。
[DependsOn(typeof(AbpAspNetCoreMvcModule), typeof(AbpIdentityDomainModule))]
public class AbpAdminTokenGatewayModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        services.Configure<TokenGatewayOptions>(context.Services.GetConfiguration().GetSection(TokenGatewayOptions.Section));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<KeyStore>();
        services.AddSingleton<Wallet>();
        services.AddSingleton<UsageLog>();
        services.AddSingleton<PlanCatalog>();
        services.AddSingleton<OrderBook>();
        services.AddSingleton<ChatBilling>();
        services.AddHttpClient(OneApiUpstream.ClientName);
        services.AddSingleton<IUpstream>(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TokenGatewayOptions>>().Value;
            return o.Upstream.Mode == UpstreamMode.Mock
                ? new MockUpstream()
                : ActivatorUtilities.CreateInstance<OneApiUpstream>(sp);
        });
    }
}
