using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.TokenGateway;

// HarnessBot 路线 C 外层：用户 Key、代币钱包、充值单、价表、OpenAI 兼容反代。
[DependsOn(typeof(AbpAdminEntityFrameworkCoreModule), typeof(AbpAspNetCoreMvcModule), typeof(AbpIdentityDomainModule))]
public class AbpAdminTokenGatewayModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = services.GetConfiguration();
        services.Configure<TokenGatewayOptions>(configuration.GetSection(TokenGatewayOptions.Section));

        services.AddDbContextFactory<TokenGatewayDbContext>((sp, b) =>
            TokenGatewayDbContext.ConfigureNpgsql(b,
                sp.GetRequiredService<IConfiguration>().GetConnectionString(TokenGatewayConsts.ConnectionStringName)
                ?? throw new InvalidOperationException("TokenGateway 需要 ConnectionStrings:Default")));
        services.AddTransient<IAbpAdminDbSchemaMigrator, TokenGatewayDbSchemaMigrator>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IKeyProtector>(sp => new AbpKeyProtector(sp.GetRequiredService<IStringEncryptionService>()));
        services.AddSingleton<KeyStore>();
        services.AddSingleton<Wallet>();
        services.AddSingleton<UsageLog>();
        services.AddSingleton<PlanCatalog>();
        services.AddSingleton<OrderBook>();
        services.AddSingleton<ChatBilling>();
        services.AddHttpClient(OneApiUpstream.ClientName);
        services.AddSingleton<IUpstream>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<TokenGatewayOptions>>().Value;
            return o.Upstream.Mode == UpstreamMode.Mock
                ? new MockUpstream()
                : ActivatorUtilities.CreateInstance<OneApiUpstream>(sp);
        });
    }
}
