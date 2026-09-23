using System;
using System.Linq;
using AbpAdmin.OpenIddict;
using AbpAdmin.Permissions;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.Mapperly;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;
using Volo.Abp.TextTemplating.Scriban;
using Volo.Abp.BlobStoring;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.BackgroundJobs;
using EasyAbp.Abp.DataDictionary;
using EasyAbp.FileManagement;
using EasyAbp.Abp.SettingUi;
using EasyAbp.NotificationService;
using EasyAbp.PaymentService;
using EasyAbp.PaymentService.Prepayment;
using EasyAbp.PaymentService.WeChatPay;
using HttpAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;

namespace AbpAdmin;

[DependsOn(
    typeof(AbpAdminDomainModule),
    typeof(AbpAdminApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule),
    typeof(AbpTextTemplatingScribanModule),
    typeof(AbpBlobStoringModule),
    typeof(AbpVirtualFileSystemModule),
    typeof(AbpBackgroundJobsModule),
    typeof(FileManagementApplicationModule),
    typeof(AbpSettingUiApplicationModule),
    typeof(NotificationServiceApplicationModule),
    typeof(PaymentServiceApplicationModule),
    typeof(PaymentServicePrepaymentApplicationModule),
    typeof(PaymentServiceWeChatPayApplicationModule)
    )]
public class AbpAdminApplicationModule : AbpModule
{
    /// <summary>
    /// 应用内回环调用 /connect/token 的命名 HttpClient（T2.7 impersonation、T2.9 GenerateAccessToken）。
    /// 开发环境 AuthServer:Authority 指向 https://localhost:44395（自签 dev-cert），
    /// 操作系统不信任该证书，默认 handler 的 SSL 校验会失败，故 Development 下跳过校验。
    /// </summary>
    public const string AuthServerTokenExchangeHttpClient = "AuthServerTokenExchange";

    /* EasyAbp.FileManagement 的 BasicFileOperationAuthorizationHandler 由
     * AdminFileOperationAuthorizationHandler 通过 [Dependency(ReplaceServices=true)]
     * + [ExposeServices(typeof(BasicFileOperationAuthorizationHandler))] 替换，
     * 无需在此手动移除。详见该 handler 文件头注释。 */
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // T2.9：OpenIddictApplicationAppService.GenerateAccessToken 依赖 IHttpClientFactory
        // （AddHttpClient 内部是 TryAdd，重复调用安全）。
        // "Turnstile" 命名客户端唯一注册点在 Domain 的 CachingAndTenancyConfigurator
        // （消费方 TurnstileCaptchaValidator 在 Domain，注册跟随消费方，round4 收敛重复注册）。
        context.Services.AddHttpClient();

        // T2.8 SaaS Pro 缺口：租户/版本级功能编辑的授权策略。
        // 开源 FeatureAppService 只对 Host 功能（T + providerKey == null，严格 null 比较）
        // 硬编码 ManageHostFeatures；其余 provider 未配置策略时抛
        // "No policy defined to get/set permissions for the provider"。
        // 注意：前端传 providerKey=（空字符串）不等于 null——绑定成 "" 后会落到
        // ProviderPolicies["T"]（而非 Host 分支），既错授权又写入 (T,"") 死行；
        // 前端 features.ts 对 undefined key 整体省略该参数（见 providerKeyParams）。
        // 映射对齐 Pro SaaS 模块：T → 租户管理权限，E → 版本管理权限（均为 Host 侧权限）。
        // 放在本模块而非 Domain：AbpAdminPermissions 常量在 Application.Contracts，Domain 引不到。
        context.Services.Configure<FeatureManagementOptions>(options =>
        {
            options.ProviderPolicies[TenantFeatureValueProvider.ProviderName] =
                TenantManagementPermissions.Tenants.ManageFeatures;
            options.ProviderPolicies[EditionFeatureValueProvider.ProviderName] =
                AbpAdminPermissions.Editions.ManageFeatures;
        });

        var builder = context.Services.AddHttpClient(AuthServerTokenExchangeHttpClient);
        // 显式 30 秒超时：HttpClient 默认 100 秒，/connect/token 是登录链路上的同步回环调用，
        // 挂 100 秒会把登录请求一起拖死；30 秒足够覆盖慢握手 + token 签发（要调长走配置）。
        // BaseAddress 供 IConnectTokenApi 声明式接口的相对地址 connect/token 拼接
        // （HttpAgent 底层用本命名 HttpClient 发请求，等价于原先运行时 authority + "/connect/token" 字符串拼接）。
        // 捕获 IConfiguration 而非快照值：ConfigureHttpClient 随 handler 过期重建（约 2 分钟）重跑，
        // 每次重读配置，与两个 Exchanger 的运行时守卫保持同源（避免启动快照与守卫分裂）。
        var configuration = context.Services.GetConfiguration();
        builder.ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            var authority = configuration["AuthServer:Authority"];
            if (!authority.IsNullOrWhiteSpace())
            {
                client.BaseAddress = new Uri(authority!.TrimEnd('/') + "/");
            }
        });
        if (context.Services.GetSingletonInstance<IAbpHostEnvironment>().IsDevelopment())
        {
            builder.ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        // T3.4：模块的 DataDictionaryRenderer 构造函数要 IList<IDataDictionaryValueProvider>。
        // Autofac 能隐式解析 IList<T>，MS.DI 不能——宿主的服务图校验（HostServiceGraphValidationTests）
        // 用 MS.DI 静态构建全部注册，缺这条会让 Renderer 及其消费方（UserExcelBuilder/UserExportJob）
        // 被判不可构建。显式注册工厂后两个容器都能构建，内容与 Autofac 的隐式列表解析等价。
        context.Services.AddTransient<System.Collections.Generic.IList<IDataDictionaryValueProvider>>(sp =>
            sp.GetServices<IDataDictionaryValueProvider>().ToList());

        // 出站 HTTP（HttpAgent）：/connect/token 声明式接口随其命名客户端就近注册（注册跟随消费方；
        // 基础设施与 Domain 层声明式接口在 HttpRemoteConfigurator，二者共同锚定 AddHttpRemote 重复调用安全性）。
        // 容错转换器：网关错误页等非 JSON 体不炸穿，Result 落 null 由交换器回落 "HTTP {status}"
        context.Services.AddHttpRemote(builder =>
        {
            builder.AddHttpDeclarative<IConnectTokenApi>();
            builder.AddHttpContentConverters(() => new HttpAgent.IHttpContentConverter[]
            {
                new HttpRemote.TolerantJsonContentConverter<ConnectTokenEndpointResponse>()
            });
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        // T3.4 第 3 步：模块的特性扫描不自动执行，必须手动触发。
        // ScanRules 是同步、一次只吃一个程序集、有返回值——返回值必须 AddRange 进
        // AbpDataDictionaryOptions.Rules 才生效（光扫不算数，漏了这一句与扫错程序集一样是静默无渲染）。
        // 扫的是放着 [DictionaryCodeField]/[DictionaryRenderField] 特性的 DTO 所在程序集。
        var loader = context.ServiceProvider.GetRequiredService<IDataDictionaryLoader>();
        var options = context.ServiceProvider
            .GetRequiredService<IOptions<AbpDataDictionaryOptions>>().Value;

        var rules = loader.ScanRules(typeof(AbpAdminApplicationModule).Assembly)
            .Concat(loader.ScanRules(typeof(AbpAdminApplicationContractsModule).Assembly))
            .ToList();

        options.Rules.AddRange(rules);

        // 验收点：扫到的规则数必须大于 0，否则说明扫错了程序集（静默失效很难查）
        context.ServiceProvider
            .GetRequiredService<ILogger<AbpAdminApplicationModule>>()
            .LogInformation("DataDictionary: registered {Count} render rules.", rules.Count);
    }
}
