using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.TokenGateway.Core;
using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Encryption;

namespace AbpAdmin.TokenGateway;

// HarnessBot 路线 C 外层：用户 Key、代币钱包、充值单、价表、OpenAI 兼容反代。
// DependsOn Domain：登录端点复用宿主 OperationRateLimiting（IOperationRateLimitingChecker 在 Domain）。
[DependsOn(typeof(AbpAdminDomainModule), typeof(AbpAdminEntityFrameworkCoreModule), typeof(AbpAspNetCoreMvcModule), typeof(AbpIdentityDomainModule))]
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
        services.AddSingleton<ITgUserGate, IdentityUserGate>();
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

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        // hb- Key 密文口令 fail-fast：IStringEncryptionService 的口令来自 StringEncryption:DefaultPassPhrase，
        // 不配置时是 ABP 源码公开常量——生产库一旦泄漏（备份/注入/共享缓存），攻击者可用公开口令
        // 解出全部用户的长期付费网关 Key。与宿主对 admin 默认密码同一姿态：非 Development
        // 且未显式配置（或仍为框架默认值）直接拒绝启动。Security.json（不入库）或环境变量注入。
        // 环境判定用 ABP 的 IAbpHostEnvironment（KernelModule 注册，控制台 DbMigrator 与 Web 宿主都有；
        // （IWebHostEnvironment/IHostEnvironment 在迁移器控制台宿主均未注册，启动即炸）
        if (!context.ServiceProvider.GetRequiredService<Volo.Abp.IAbpHostEnvironment>().IsDevelopment())
        {
            var pass = context.GetConfiguration()["StringEncryption:DefaultPassPhrase"];
            var abpDefault = new AbpStringEncryptionOptions().DefaultPassPhrase;
            // compose 提交的字面量回退（docker-compose.yml 的 dev-only-change-me）同样视为
            // 未显式配置（ABP 框架常量与仓库清单默认值同样视为未配置）。
            const string composeDevDefault = "dev-only-change-me";
            if (string.IsNullOrWhiteSpace(pass) || pass == abpDefault || pass == composeDevDefault)
            {
                throw new AbpInitializationException(
                    "TokenGateway：非 Development 环境必须显式配置 StringEncryption:DefaultPassPhrase" +
                    "（Configuration/Security.json 或环境变量 StringEncryption__DefaultPassPhrase），" +
                    "否则 hb- Key 以公开默认口令（ABP 常量或仓库 compose 回退值）加密，库泄漏即 Key 全泄。");
            }
        }
        base.OnApplicationInitialization(context);
    }
}
