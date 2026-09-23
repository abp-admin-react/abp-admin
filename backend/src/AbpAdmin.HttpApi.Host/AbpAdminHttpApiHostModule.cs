using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;
using OpenIddict.Server.AspNetCore;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Files;
using AbpAdmin.MultiTenancy;
using AbpAdmin.HealthChecks;
using AbpAdmin.AuditLogs;
using AbpAdmin.Elasticsearch;
using AbpAdmin.ClickHouse;
using Microsoft.OpenApi;
using Volo.Abp;
using Volo.Abp.Studio;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Autofac;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using AbpAdmin.Data;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.OpenIddict.WildcardDomains;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using AspNet.Security.OAuth.Weixin;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Swashbuckle;
using Volo.Abp.Studio.Client.AspNetCore;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;
using Volo.Abp.AspNetCore.Mvc.Libs;
using Volo.Abp.Security.Claims;
using AbpAdmin.TextTemplates;
using AbpAdmin.Controllers;
using AbpAdmin.Desensitization;
using AbpAdmin.ExtensionGrants;
using AbpAdmin.ExternalLogins;
using AbpAdmin.Gdpr;
using AbpAdmin.Identity;
using AbpAdmin.OpenIddict;
using AbpAdmin.OperationLogs;
using AbpAdmin.ScheduledJobs;
using AbpAdmin.SignalR;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BackgroundWorkers.Quartz;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Quartz;
using Medallion.Threading;
using Medallion.Threading.Redis;
using StackExchange.Redis;

namespace AbpAdmin;

[DependsOn(
    typeof(AbpAdminHttpApiModule),
    typeof(AbpStudioClientAspNetCoreModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(AbpAdminApplicationModule),
    typeof(AbpAdminEntityFrameworkCoreModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpAspNetCoreSignalRModule),
    // T3.3：只替换 worker 调度器为 Quartz（cron 能力），一次性队列作业（IBackgroundJobManager）
    // 继续走 EF 存储，BackgroundJobAppService 与 AbpBackgroundJobs 表不受影响。
    // 放 Host 而不是 Domain：worker 调度器是宿主级基础设施，DbMigrator 不应起调度器。
    typeof(AbpBackgroundWorkersQuartzModule),
    // T5：ES/ClickHouse 辅助存储（宿主级基础设施；模块内部按配置开关，未启用即无副作用）
    typeof(AbpAdminElasticsearchModule),
    typeof(AbpAdminClickHouseModule)
    )]
public class AbpAdminHttpApiHostModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("AbpAdmin");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        // 账号切换：客户端带 prompt=select_account 发起授权时，
        // ABP AuthorizeController 跳到该页让用户选"继续当前账号 / 换账号登录"
        PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
        {
            options.SelectAccountPage = "/Account/SelectAccount";
        });

        PreConfigure<AbpOpenIddictWildcardDomainOptions>(options =>
        {
            options.EnableWildcardDomainSupport = true;
            options.WildcardDomainsFormat.Add("http://{0}.localhost:8000");
            options.WildcardDomainsFormat.Add("https://{0}.localhost:8000");
            options.WildcardDomainsFormat.Add("http://{0}.localhost:8000/user/callback");
            options.WildcardDomainsFormat.Add("http://{0}.localhost:8000/user/login");
        });

        // T2.7: 注册无密码登录与模拟登录扩展授权类型；LinkAccounts: 关联账号切换
        PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
        {
            serverBuilder.Configure(options =>
            {
                options.GrantTypes.Add(PasswordlessTokenExtensionGrant.ExtensionGrantName);
                options.GrantTypes.Add(ImpersonationTokenExtensionGrant.ExtensionGrantName);
                options.GrantTypes.Add(LinkedAccountExtensionGrant.ExtensionGrantName);
            });

            // T2.8: 登录时校验租户激活状态（Passive/已到期拒绝签发令牌）
            serverBuilder.AddEventHandler(TenantActivationOpenIddictServerHandler.Descriptor);
            serverBuilder.AddEventHandler(IdentitySessionOpenIddictServerHandler.Descriptor);
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", configuration["AuthServer:CertificatePassPhrase"]!);
                serverBuilder.SetIssuer(new Uri(configuration["AuthServer:Authority"]!));
            });
        }

        // T3.3 第 2 步：Quartz 持久化与 clustering。
        // 必须 PreConfigure 而不是 Configure：AbpQuartzModule.ConfigureServices 通过
        // ExecutePreConfiguredActions<AbpQuartzOptions>() 读取 Configurator，
        // Configure 注册的委托执行太晚，会静默失效（05-reference-facts 9.2，已核实）。
        var usePersistentStore = configuration.GetValue<bool>("Quartz:UsePersistentStore");
        PreConfigure<AbpQuartzOptions>(options =>
        {
            options.Configurator = configure =>
            {
                configure.SchedulerName = configuration["Quartz:SchedulerName"] ?? "AbpAdminScheduler";
                // clustering 下每个实例必须有唯一 id，AUTO 让 Quartz 生成
                configure.SchedulerId = "AUTO";

                if (!usePersistentStore)
                {
                    // 开发环境（SQLite）走内存 store，不需要 QRTZ_ 表。
                    // 代价：开发环境测不出 clustering 问题，多实例验收必须在 PostgreSQL 做。
                    return;
                }

                // Quartz 3.15.0 扩展方法已核实（本机 NuGet 缓存程序集 grep）：
                // UsePersistentStore/UseProperties/UseClustering 在 Quartz.dll，
                // UsePostgres 在 Quartz.dll 的 AdoProviderExtensions，
                // UseSystemTextJsonSerializer 在 Quartz.Serialization.SystemTextJson.dll。
                configure.UsePersistentStore(store =>
                {
                    // JobDataMap 只存字符串键值不做对象序列化。动态 worker 只放一个
                    // worker 名字符串（05 14.6 已核实），完全兼容。
                    store.UseProperties = true;
                    store.UseSystemTextJsonSerializer();
                    store.UseClustering(cluster =>
                    {
                        cluster.CheckinInterval = TimeSpan.FromSeconds(20);
                        cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(60);
                    });
                    store.UsePostgres(postgres =>
                    {
                        postgres.ConnectionString = configuration.GetConnectionString("Default")!;
                        postgres.TablePrefix = "QRTZ_";
                    });
                });
            };
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        // 默认安全（问题3 修复）：PII 日志改白名单语义——仅开发环境或显式开启 App:EnablePiiLogging 时输出
        // （排查 JWT 错误用）。原 App:DisablePII 默认 false → 取反后生产默认全开，双重反直觉。
        if (hostingEnvironment.IsDevelopment() || configuration.GetValue<bool>("App:EnablePiiLogging"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
        }

        // 完整安全工件（令牌原文/JWT 头载荷）泄漏面更大，仅专项排障时由配置显式打开（同样默认关）。
        if (configuration.GetValue<bool>("App:EnableSecurityArtifactLogging"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.LogCompleteSecurityArtifact = true;
        }

        if (!configuration.GetValue<bool>("AuthServer:RequireHttpsMetadata"))
        {
            Configure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });
            
            Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
        }

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });

            context.Services.AddRazorPages()
                .AddRazorRuntimeCompilation();
        }

        // T2.7: 注册扩展授权处理器
        Configure<AbpOpenIddictExtensionGrantsOptions>(options =>
        {
            options.Grants.Add(PasswordlessTokenExtensionGrant.ExtensionGrantName, new PasswordlessTokenExtensionGrant());
            options.Grants.Add(ImpersonationTokenExtensionGrant.ExtensionGrantName, new ImpersonationTokenExtensionGrant());
            options.Grants.Add(LinkedAccountExtensionGrant.ExtensionGrantName, new LinkedAccountExtensionGrant());
        });

        // T2.7: Razor Pages 的 PageModel 由 ABP 的 ServiceBasedPageModelActivatorProvider 从容器激活，
        // 替换的登录页模型与双因素验码页模型必须显式注册
        context.Services.AddTransient<Pages.Account.AbpAdminLoginModel>();
        context.Services.AddTransient<Pages.Account.TwoFactorVerificationModel>();
        context.Services.AddTransient<Pages.Account.LinkLoginModel>();

        ConfigureStudio(hostingEnvironment);
        ConfigureAuthentication(context);
        ConfigureAntiforgery();
        ConfigureUrls(configuration);
        ConfigureMultiTenancy();
        ConfigureBundles(hostingEnvironment);
        ConfigureConventionalControllers();
        ConfigureDefaultDenyAuthorization(context);
        ConfigureHealthChecks(context);
        ConfigureSwagger(context, configuration);
        ConfigureVirtualFileSystem(context);
        ConfigureCors(context, configuration);
        ConfigureRedis(context, configuration);
        ConfigureSignalRBackplane(context, configuration);

        // 前后端分离 SPA 场景不使用 MVC 客户端库，禁用 wwwroot/libs 检查
        Configure<AbpMvcLibsOptions>(options =>
        {
            options.CheckLibs = false;
        });

        // T2.4: 绑定 Cookie Consent 选项并下发到 application-configuration 端点
        Configure<AbpAdminCookieConsentOptions>(configuration.GetSection(AbpAdminCookieConsentOptions.SectionName));
        Configure<AbpApplicationConfigurationOptions>(options =>
        {
            options.Contributors.Add(new CookieConsentApplicationConfigurationContributor());
            // T3.2: 下发 SignalR:Enabled，前端读到 false 直接不尝试连接（降级轮询）
            options.Contributors.Add(new SignalRApplicationConfigurationContributor());
        });

        // 文件上传体积限制：与 FileManagement:MaxByteSizeForEachUpload 对齐，
        // 否则 Kestrel / 表单解析会先于模块约束拒绝请求。
        // 若部署在 Nginx 后，还需同步设置 client_max_body_size。
        var maxUploadBytes = configuration.GetValue<long>("FileManagement:MaxByteSizeForEachUpload", 200L * 1024 * 1024);
        Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = maxUploadBytes;
        });
        context.Services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = maxUploadBytes;
        });

        // T2.1 审计日志：打开实体变更记录。
        // AddAllEntities + 落库前脱敏：全实体变更喂给实体历史 UI，但口令哈希/令牌等
        // 敏感属性值由 SensitiveEntityChangeScrubbingContributor 在保存前抹掉
        // （PostContributors 在 IAuditingStore.SaveAsync 之前执行）。
        Configure<AbpAuditingOptions>(options =>
        {
            options.IsEnabled = true;
            // 默认 false。开启后 GET 请求也会产生审计日志，日志量会明显上涨，
            // 是否开启由部署方决定，这里做成可配置而不是写死。
            options.IsEnabledForGetRequests = configuration.GetValue("Auditing:IsEnabledForGetRequests", false);

            // SettingUi 写值入参 Dictionary<string,string> 含明文新密钥（SMTP 密码/短信 SecretKey），
            // 且上游 SettingUiController 挂不了 [DisableAuditing]（控制器层审计记录拦不住），
            // 只能在序列化处整体忽略该参数类型——"谁改了什么"的语义轨迹由
            // AbpAdminSettingUiAppService 内 IOperationLogWriter 手写（只记设置名）。
            // 影响面：仅审计日志的参数序列化，其他 Dictionary<string,string> 入参的服务也会丢参数明细。
            options.IgnoredTypes.Add(typeof(Dictionary<string, string>));

            // 全实体变更记录（AbpEntityChanges 因此有数据，实体历史功能依赖它）
            options.EntityHistorySelectors.AddAllEntities();

            // 敏感属性（IdentityUser.PasswordHash / Token.Payload / ClientSecret 等）落库前脱敏
            options.Contributors.Add(new SensitiveEntityChangeScrubbingContributor());
        });

        ConfigureDesensitization(context);
        ConfigureOperationLogging(context);
    }

    /// <summary>
    /// 数据脱敏（对标 ruoyi-vue-pro desensitize）：MVC 响应序列化时对命中规则的 string 字段脱敏。
    /// IdentityUserDto 是 ABP 包内类型无法加注解，走注册表规则；自有 DTO 直接打 [Masked]。
    /// 明文权限沿用 AbpIdentity.Users.Update——能改用户的人才能看明文（与 ruoyi 的 disable SpEL 同语义，
    /// 也避免了「列表里看到掩码、编辑表单提交掩码」的回写事故）。
    /// </summary>
    private void ConfigureDesensitization(ServiceConfigurationContext context)
    {
        var registry = new MaskingRuleRegistry()
            .Add<IdentityUserDto>(u => u.PhoneNumber,
                new MaskedAttribute(MaskKindEnum.Mobile) { PlaintextPermission = IdentityPermissions.Users.Update })
            .Add<IdentityUserDto>(u => u.Email,
                new MaskedAttribute(MaskKindEnum.Email) { PlaintextPermission = IdentityPermissions.Users.Update });
        context.Services.AddSingleton(registry);
        context.Services.Configure<JsonOptions>(options =>
            MaskingJsonSetup.Configure(options.JsonSerializerOptions, registry));
    }

    /// <summary>
    /// 语义化操作日志（对标 mzt-biz-log @LogRecord）：全局 ActionFilter 读取应用服务方法上的
    /// [OperationLog] 注解，方法执行后渲染模板并独立 UoW 落库；无注解的请求只查一次缓存字典。
    /// </summary>
    private void ConfigureOperationLogging(ServiceConfigurationContext context)
    {
        context.Services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<OperationLogActionFilter>();
        });
    }

    private void ConfigureRedis(ServiceConfigurationContext context, IConfiguration configuration)
    {
        // Redis:IsEnabled 语义必须与 ABP 缓存模块（AbpCachingStackExchangeRedisModule）及
        // CacheMonitorRedisConnection 对齐：键缺失/为空=启用（由 Redis:Configuration 是否配置决定），
        // 仅显式 false 才关闭。此前 GetValue 默认 false 的写法与缓存模块「缺省=开」相矛盾——
        // 只配 Redis:Configuration 不写 IsEnabled 的部署会得到「缓存走 Redis、分布式锁退化为
        // 进程内」的静默不一致，多实例下锁形同虚设。
        // 值非法（如 "yes"）直接启动失败而不是静默当作关闭——多实例部署里那等于把分布式锁
        // 静默关掉（安全层审查指出的陷阱）。
        var redisEnabledRaw = configuration["Redis:IsEnabled"];
        var redisEnabled = true;
        if (!string.IsNullOrEmpty(redisEnabledRaw))
        {
            if (!bool.TryParse(redisEnabledRaw, out redisEnabled))
            {
                throw new AbpInitializationException(
                    $"Configuration value \"Redis:IsEnabled\" is not a valid boolean: \"{redisEnabledRaw}\". " +
                    "Use true/false, or remove the key (absent = enabled when Redis:Configuration is set).");
            }
        }

        if (!redisEnabled)
        {
            return;
        }

        var redisConfiguration = configuration["Redis:Configuration"];
        if (string.IsNullOrWhiteSpace(redisConfiguration))
        {
            return;
        }

        context.Services.AddSingleton<IDistributedLockProvider>(_ =>
        {
            var connection = ConnectionMultiplexer.Connect(redisConfiguration);
            return new RedisDistributedSynchronizationProvider(connection.GetDatabase());
        });
        context.Services.AddTransient<IAbpDistributedLock, MedallionAbpDistributedLock>();
    }

    /// <summary>
    /// T3.2：SignalR Redis backplane（多实例推送正确送达）。
    /// 开关与 Redis:IsEnabled 完全正交——唯一有效判据是 SignalR:UseRedisBackplane
    /// （Redis:Configuration 在本仓库恒非空）。退化行为是"进程内广播"（DefaultHubLifetimeManager），
    /// 不是启动失败：功能正常，只是广播范围只有本进程。
    /// ChannelPrefix 用环境名派生（修法①，不新增必填配置键）：多环境共用一台 Redis 时
    /// 不设前缀会互相串消息。规格原文的 Redis:InstanceName 在本仓库不存在，会静默退回 Default。
    /// backplane 保持默认的自建连接，不复用 ABP 缓存/锁那条 multiplexer（性能隔离，
    /// 订阅背压不应影响缓存延迟）；AbpAspNetCoreSignalRModule 内部的 AddSignalR() 全部走
    /// TryAdd*，这里再调一次是安全的（不会挤掉 ABP 注册的三个 Hub filter）。
    /// </summary>
    private void ConfigureSignalRBackplane(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var redisConfiguration = configuration["Redis:Configuration"];
        if (redisConfiguration.IsNullOrWhiteSpace() ||
            !configuration.GetValue<bool>("SignalR:UseRedisBackplane"))
        {
            return;
        }

        var environmentName = context.Services.GetHostingEnvironment().EnvironmentName;

        context.Services
            .AddSignalR()
            .AddStackExchangeRedis(redisConfiguration!, options =>
            {
                options.Configuration.ChannelPrefix =
                    RedisChannel.Literal($"AbpAdmin:SignalR:{environmentName}");
            });
    }

    /// <summary>
    /// T3.2：SPA 的 WebSocket/SSE 不允许设自定义请求头（浏览器规范硬限制），token 只能走
    /// query string 的 access_token。本中间件在认证前把它搬进 Authorization 头，
    /// 并随即从 QueryString 里剥除——保护管道下游（审计 URL、后续日志）。
    /// 只对 Hub 路径前缀生效是安全要求（不给整个 API 开"token 走 query string"的口子），
    /// 不是优化。
    /// 【渗透测试修复】OpenIddict 校验默认还会从任意请求的 query 提取 access_token（实测
    /// GET /api/app/audit-log?access_token=... 返回 200）——令牌会泄漏进代理/访问日志与
    /// Referer。因此对【非 Hub】路径在认证前强制剥除 access_token：hub 之外 query 带 token
    /// 一律不认证（fail-closed），hub 仍走上面的搬运通道。
    /// 已验证（T3.2 e2e）：改写 Request.QueryString 后 SignalR negotiate 与 WebSocket
    /// 握手不受影响——QueryFeature 在 QueryString 变化后会重新解析，传输握手读的 id 参数保留。
    /// 注意：Hosting 层的 "Request starting" 在任何中间件之前就用原始 QueryString 记录，
    /// 这里的剥离盖不住它——那一层由 Program.cs 里挂的
    /// SignalRQueryStringSanitizingEnricher 在日志出口脱敏。
    /// </summary>
    private const string SignalRHubPathPrefix = "/signalr-hubs";

    private static void UseSignalRQueryStringAuthentication(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(
                    SignalRHubPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var accessToken = context.Request.Query["access_token"].FirstOrDefault();

                if (!accessToken.IsNullOrWhiteSpace())
                {
                    if (!context.Request.Headers.ContainsKey(HeaderNames.Authorization))
                    {
                        context.Request.Headers.Authorization = $"Bearer {accessToken}";
                    }

                    var query = QueryHelpers.ParseQuery(context.Request.QueryString.Value);
                    query.Remove("access_token");
                    context.Request.QueryString = QueryString.Create(
                        query.SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));
                }
            }
            else if (context.Request.Query.ContainsKey("access_token"))
            {
                // OpenIddict 校验默认从 query 提取 access_token（渗透测试实测可认证任意 API）。
                // 非 Hub 路径没有携带它的合法理由：认证前剥除，让这类请求只能走 Authorization 头。
                var query = QueryHelpers.ParseQuery(context.Request.QueryString.Value);
                query.Remove("access_token");
                context.Request.QueryString = QueryString.Create(
                    query.SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));
            }

            await next(context);
        });
    }

    private void ConfigureStudio(IHostEnvironment hostingEnvironment)
    {
        if (hostingEnvironment.IsProduction())
        {
            Configure<AbpStudioClientOptions>(options =>
            {
                options.IsLinkEnabled = false;
            });
        }
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });

        // 渗透测试修复（安全层复核补齐）：OpenIddict 校验默认还从 query string 与 form body
        // 提取 access_token——只靠中间件剥 query 关不完（form body 仍可认证任意 POST API）。
        // 从源头只保留 Authorization 头提取；SignalR hub 的 query token 由本模块的
        // UseSignalRQueryStringAuthentication 在认证前搬进 Authorization 头，不受影响。
        context.Services.Configure<OpenIddictValidationAspNetCoreOptions>(options =>
        {
            options.DisableAccessTokenExtractionFromQueryString = true;
            options.DisableAccessTokenExtractionFromBodyForm = true;
        });

        // 渗透测试修复：/api 路径匿名或 Cookie 过期时，Identity Cookie 的默认挑战是 302 跳
        // 登录页——API 语义应为 401/403（此前实测匿名 GET /api/app/audit-log 返回 302）。
        // 非 /api 路径（MVC 账户页等）保持原有重定向行为。
        context.Services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = redirectContext =>
            {
                if (redirectContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    redirectContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                }
                else
                {
                    redirectContext.Response.Redirect(redirectContext.RedirectUri);
                }

                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = redirectContext =>
            {
                if (redirectContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    redirectContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                }
                else
                {
                    redirectContext.Response.Redirect(redirectContext.RedirectUri);
                }

                return Task.CompletedTask;
            };
        });

        // T2.7: 外部登录 + 每租户动态配置
        // 注册 GitHub OAuth（占位 ClientId/ClientSecret，实际值由 DynamicExternalLoginOptionsManager 从 Setting 读取）
        context.Services.AddAuthentication()
            .AddGitHub(options =>
            {
                options.ClientId = "placeholder";
                options.ClientSecret = "placeholder";
                options.CallbackPath = "/signin-github";
            })
            .AddMicrosoftAccount(options =>
            {
                options.ClientId = "placeholder";
                options.ClientSecret = "placeholder";
                options.CallbackPath = "/signin-microsoft";
            })
            .AddWeixin(options =>
            {
                options.ClientId = "placeholder";
                options.ClientSecret = "placeholder";
                options.CallbackPath = "/signin-weixin";
            })
            .AddGoogle(options =>
            {
                options.ClientId = "placeholder";
                options.ClientSecret = "placeholder";
                options.CallbackPath = "/signin-google";
            });

        context.Services.AddAbpDynamicOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions, DynamicExternalLoginOptionsManager<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>>();
        context.Services.AddAbpDynamicOptions<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions, DynamicExternalLoginOptionsManager<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>>();
        context.Services.AddAbpDynamicOptions<AspNet.Security.OAuth.Weixin.WeixinAuthenticationOptions, DynamicExternalLoginOptionsManager<AspNet.Security.OAuth.Weixin.WeixinAuthenticationOptions>>();
        context.Services.AddAbpDynamicOptions<Microsoft.AspNetCore.Authentication.Google.GoogleOptions, DynamicExternalLoginOptionsManager<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>>();

        var authority = context.Services.GetConfiguration()["AuthServer:Authority"] ?? "https://localhost:44395";
        if (Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri))
        {
            Configure<IdentityPasskeyOptions>(options =>
            {
                options.ServerDomain = authorityUri.Host;
            });
        }

        context.Services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            var previous = options.Events.OnRedirectToLogin;
            options.Events.OnRedirectToLogin = async redirectContext =>
            {
                var currentTenant = redirectContext.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
                if (currentTenant.Id.HasValue)
                {
                    redirectContext.RedirectUri = QueryHelpers.AddQueryString(
                        redirectContext.RedirectUri,
                        "__tenant",
                        currentTenant.Id.Value.ToString());
                }

                if (previous != null)
                {
                    await previous(redirectContext);
                    return;
                }

                redirectContext.Response.Redirect(redirectContext.RedirectUri);
            };
        });
    }

    private void ConfigureAntiforgery()
    {
        // ABP 官方默认（AutoValidate = true）：请求带认证 Cookie 时强制校验防伪令牌——
        // 会话 Cookie 可直呼 API（ForwardIdentityAuthenticationForBearer 的设计意图），
        // CSRF 防线就落在这里。判定规则（AbpValidateAntiforgeryTokenAuthorizationFilter）：
        // 带 Identity.Application Cookie → 必须校验；无 XSRF-TOKEN Cookie（纯 API/Bearer 客户端）→ 跳过。
        // 前端 requestErrorConfig 已把 XSRF-TOKEN Cookie 回传为 RequestVerificationToken 头（官方 SPA 流程），
        // 开发代理下 SPA 与 API 同源，Cookie 均可送达，没有跨源障碍。
        Configure<AbpAntiForgeryOptions>(options =>
        {
            options.AutoValidate = true;
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.Applications["SPA"].RootUrl = configuration["App:SpaUrl"] ?? "http://{0}.localhost:8000";
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });
    }

    private void ConfigureMultiTenancy()
    {
        Configure<AbpTenantResolveOptions>(options =>
        {
            // SPA sends __tenant on authorize and API calls. That must win over the
            // tenant claim in a Host access token, otherwise subdomain logins stay on Host.
            var currentUser = options.TenantResolvers.FirstOrDefault(x => x.Name == "CurrentUser");
            var query = options.TenantResolvers.FirstOrDefault(x => x.Name == "QueryString");
            var header = options.TenantResolvers.FirstOrDefault(x => x.Name == "Header");
            if (currentUser != null && query != null)
            {
                options.TenantResolvers.Remove(query);
                options.TenantResolvers.Insert(options.TenantResolvers.IndexOf(currentUser), query);
            }
            if (currentUser != null && header != null)
            {
                options.TenantResolvers.Remove(header);
                options.TenantResolvers.Insert(options.TenantResolvers.IndexOf(currentUser), header);
            }

            options.AddDomainTenantResolver("{0}.localhost");
        });
    }

    private void ConfigureBundles(IHostEnvironment hostingEnvironment)
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                }
            );

            options.ScriptBundles.Configure(
                LeptonXLiteThemeBundles.Scripts.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-scripts.js");
                    if (hostingEnvironment.IsDevelopment())
                    {
                        bundle.AddFiles("/dev-login-helper.js");
                    }
                }
            );
        });
    }


    private void ConfigureVirtualFileSystem(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<AbpAdminDomainSharedModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAdmin.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<AbpAdminDomainModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAdmin.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<AbpAdminApplicationContractsModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAdmin.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<AbpAdminApplicationModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AbpAdmin.Application"));
            });
        }
    }

    private void ConfigureConventionalControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            // T3.1：UploadAvatarInput 内嵌 IRemoteStreamContent。ABP 默认只把 IFormFile/IRemoteStreamContent
            // 本体列入 FormBodyBindingIgnoredTypes（按 action 参数类型判定），嵌套 DTO 会被推成 [FromBody]
            // JSON 绑定，浏览器传的 multipart/form-data 在输入格式化阶段就被 415 短路。
            // 按 ABP 官方口径（support #9202）把该 DTO 加进忽略表，让参数回落到表单绑定，
            // 由 AbpRemoteStreamContentModelBinder 从 Request.Form.Files 取 File 字段。
            options.ConventionalControllers.FormBodyBindingIgnoredTypes.Add(
                typeof(AbpAdmin.Profile.UploadAvatarInput));

            options.ConventionalControllers.Create(
                typeof(AbpAdminApplicationModule).Assembly,
                settings =>
                {
                    // T2.9：GetAllAsync 按 ABP 约定会被剥掉整个 "GetAll" 前缀而落到控制器根路径，
                    // 与 GetListAsync 冲突（Swagger 报 Conflicting method/path）。
                    // 规格要求路由为 GET .../open-iddict-scope/all，这里只把这一个动作的 URL 名规范成 "all"
                    // （ActionModel.ActionName 已被 ABP 剥掉 Async 后缀，是 "GetAll"），
                    // 其它动作保持默认（返回 context.ActionNameInUrl 即恒等）。
                    settings.UrlActionNameNormalizer = context =>
                        context.ControllerName == "OpenIddictScope" &&
                        context.Action.ActionName == "GetAll"
                            ? "all"
                            : context.ActionNameInUrl;
                });
        });
    }

    private static void ConfigureSwagger(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGenWithOidc(
            configuration["AuthServer:Authority"]!,
            ["AbpAdmin"],
            [AbpSwaggerOidcFlows.AuthorizationCode],
            null,
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "AbpAdmin API", Version = "v1" });
                // NRT 可空性忠实进 OpenAPI 文档（非空引用属性进 required[]）：
                // swagger.json 是前端手写镜像层的事实源，见 FrontendContractSnapshotTests 的设计说明
                options.SupportNonNullableReferenceTypes();
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);

                // 加载各项目的 XML 注释文档，让 Swagger / 前端 openapi 生成拿到具体描述
                foreach (var xmlFile in Directory.GetFiles(AppContext.BaseDirectory, "AbpAdmin*.xml"))
                {
                    options.IncludeXmlComments(xmlFile);
                }
            });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .SetIsOriginAllowed(origin => IsAllowedSpaOrigin(origin, configuration, hostingEnvironment))
                    .WithAbpExposedHeaders()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    private static bool IsAllowedSpaOrigin(string origin, IConfiguration configuration, IHostEnvironment hostingEnvironment)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // 问题8 修复：localhost 便利放行仅在开发环境生效，优先级也调到 CorsOrigins 显式清单之后。
        // 原实现无条件放行一切 localhost 源并叠加 AllowCredentials，开发便利逻辑泄漏到生产路径。
        var isConfiguredOrigin = ConfiguredCorsOrigins(configuration).Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);
        if (isConfiguredOrigin)
        {
            return true;
        }

        return hostingEnvironment.IsDevelopment() &&
               (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ConfiguredCorsOrigins(IConfiguration configuration)
    {
        return configuration["App:CorsOrigins"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim().RemovePostFix("/"))
            .ToArray() ?? Array.Empty<string>();
    }

    private void ConfigureHealthChecks(ServiceConfigurationContext context)
    {
        context.Services.AddAbpAdminHealthChecks();
    }

    /// <summary>
    /// 默认拒绝（default-deny）：任何没有授权元数据的请求一律要求已认证。
    /// 背景是 SettingUi 裸奔事件的病根——上游模块控制器忘了加 [Authorize] 而宿主默认放行，
    /// 这类问题靠逐端点补丁永远追不完（宿主挂了 13+ 个第三方模块的 HttpApi 包）。
    /// FallbackPolicy 把它反转为系统属性：未来任何模块升级引入的裸端点最多退化到"登录即可访问"。
    /// 注意（AuthorizationMiddleware 源码核实，2026-09）：fallback 对"无端点请求"同样生效
    /// （CombineAsync 拿不到 IAuthorizeData 就取 fallback），所以中间件式的匿名出口也必须排在其前面。
    /// 显式豁免清单（都体现为端点元数据，由 AnonymousEndpointSweepTests 双向审计兜住——
    /// 正向"已匿名必登记"、反向"登录链路端点必匿名"）：
    /// - /connect/token、/connect/authorize、/connect/endsession：OpenIddictConnectEndpointsConvention
    ///   （上游控制器无任何授权标注，不豁免则登录/登出自身被拦；SPA 的 end_session_endpoint
    ///   就是 /connect/endsession）
    /// - /Account/Login 等登录链路 Razor Pages：PreLoginRazorPagesConvention
    ///   （10.6.1 页面模型无授权标注，不豁免则匿名访客打不开登录页；/Account/Manage 不在豁免列）
    /// - /api/abp/application-configuration(+localization)：PreLoginApiEndpointsConvention
    ///   （前端登录页拉取基础配置/本地化必需，10.6.1 控制器无授权标注——Chrome 实测抓到过 401）
    /// - /health-status、/health-ui、/health-api：探针面板，匿名是既有语义（NoExceptionDetails 已防回显）
    /// - wwwroot 静态资源：不含运行时数据，业界默认公开
    /// - ABP 自带 [AllowAnonymous] 端点（登录/注册前置流程等）不受影响
    /// </summary>
    private void ConfigureDefaultDenyAuthorization(ServiceConfigurationContext context)
    {
        context.Services.Configure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        context.Services.Configure<MvcOptions>(options =>
        {
            options.Conventions.Add(new Authorization.OpenIddictConnectEndpointsConvention());
            options.Conventions.Add(new Authorization.PreLoginApiEndpointsConvention());
        });

        context.Services.Configure<Microsoft.AspNetCore.Mvc.RazorPages.RazorPagesOptions>(options =>
        {
            options.Conventions.Add(new Authorization.PreLoginRazorPagesConvention());
        });
    }


    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        // 全自动建表（开关 Database:AutoMigrateOnStartup，默认开）：启动时自动判断 schema 是否就绪——
        // 有未应用迁移（新库表缺失 / 版本落后）才执行迁移+种子，schema 已就绪则直接跳过，不拖慢启动。
        // 完整迁移流程内部含自动建库（SQLite 自动建文件；PG 经维护库 CREATE DATABASE）。
        // 生产多实例部署若担心启动竞争，可用环境变量 Database__AutoMigrateOnStartup=false 关闭，改由 DbMigrator 负责。
        var autoMigrate = context.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetValue<bool>("Database:AutoMigrateOnStartup");
        if (autoMigrate)
        {
            using (var scope = context.ServiceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AbpAdminDbContext>();

                // 库不可读（数据库不存在等）时 pending 检查本身会抛错——视为"需要迁移"，走完整流程（含自动建库）
                bool needsMigration;
                try
                {
                    needsMigration = (await dbContext.Database.GetPendingMigrationsAsync()).Any();
                }
                catch (Exception)
                {
                    needsMigration = true;
                }

                if (needsMigration)
                {
                    await scope.ServiceProvider
                        .GetRequiredService<AbpAdminDbMigrationService>()
                        .MigrateAsync();
                }
                else
                {
                    context.ServiceProvider.GetRequiredService<ILogger<AbpAdminHttpApiHostModule>>()
                        .LogInformation("数据库 schema 已就绪（无待应用迁移），跳过自动迁移");
                }
            }
        }

        // 脱敏 converter 无法从 DI 拿请求级服务，经静态门引用单例（fail-closed：未设置则一律脱敏）
        MaskingPermissionGate.Current = context.ServiceProvider.GetRequiredService<MaskingPermissionGate>();

        app.UseForwardedHeaders();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // CorrelationId：官方启动模板的管线第一环（Volo.Abp.AspNetCore.Tracing 的
        // AbpCorrelationIdMiddleware——读/生成 X-Correlation-Id，经 ICorrelationIdProvider
        // 异步流动，官方文档说明它会自动传播到审计日志、安全日志与 Serilog）。
        // 此前缺失：ICorrelationIdProvider.Get() 恒 null，三类日志的关联 ID 全空，
        // 操作日志与审计日志无法互相关联（Chrome 实测确认过）。
        app.UseCorrelationId();

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseErrorPage();
        }

        // D12 兜底：GDPR 下载（FileResult）/删户（void）等非 ObjectResult 动作，
        // 官方 AbpExceptionFilter/AbpExceptionHandlingMiddleware 按设计不转译其异常
        //（条件：ObjectResult / AJAX / Accept 含 json，见 AbpExceptionFilter.ShouldHandleException），
        // 带码 BusinessException 会冒泡成 500 + 堆栈裸返回。
        // 注册必须在 UseDeveloperExceptionPage/UseErrorPage **之后**（管道上更内层）：
        // 异常由内向外 unwind，内层先拿到；同时仍需在 UseRouting 之前以包住端点。
        // 仅接管带错误码的业务异常（403/429 + ABP 错误格式），其余原样放行。
        app.UseFriendlyBusinessException();

        // T-FM-02 文件上传内容防线（magic-bytes/脚本扫描），必须在路由与模型绑定之前
        app.UseFileUploadContentGuard();

        app.UseRouting();

        // 静态资源匿名公开：wwwroot 只有前端脚本/图片/样式，不含运行时数据；
        // 不豁免的话 FallbackPolicy 会把这些端点全部 401。
        app.MapAbpStaticAssets().AllowAnonymous();

        app.UseAbpStudioLink();
        app.UseAbpSecurityHeaders();
        app.UseCors();

        // T3.2：Hub 路径的 query string token 搬进 Authorization 头（必须在认证之前、Cors 之后）
        UseSignalRQueryStringAuthentication(app);

        // round4 security F1：/connect/token 的 password grant 端点级限流（防绕过页面限流+验证码的
        // 直接爆破/spraying），必须在 UseAuthentication 之前（OpenIddict 管内没有可挂的早于租户解析的钩子）。
        app.UseMiddleware<AbpAdmin.OpenIddict.TokenEndpointRateLimitingMiddleware>();

        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }

        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseMiddleware<IdentitySessionValidationMiddleware>();
        app.UseMiddleware<AbpAdmin.DataScopes.DataScopeMiddleware>();

        // 问题14 修复：Swagger UI 生产环境默认不暴露（API 目录泄漏），仅开发环境或
        // 显式配置 App:EnableSwaggerInProduction 时挂载。
        // 必须排在 UseAuthorization 之前：swagger.json/UI 是中间件式出口（无路由端点），
        // FallbackPolicy 对无端点请求同样生效（见 ConfigureDefaultDenyAuthorization 注释），
        // 放在授权之后匿名访问会 401，开发环境的匿名浏览就没了。
        var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
        if (env.IsDevelopment() || configuration.GetValue<bool>("App:EnableSwaggerInProduction"))
        {
            app.UseSwagger();
            app.UseAbpSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "AbpAdmin API");

                options.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
            });
        }

        app.UseAuthorization();

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();

        await context.AddBackgroundWorkerAsync<IdentitySessionCleanupBackgroundWorker>();

        // T2.3: 启动时同步静态模板到数据库
        using (var scope = context.ServiceProvider.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<StaticTemplateSaveWorker>();
            await worker.SaveStaticTemplatesToDatabaseAsync();
        }

        // T3.2: 打印 HubLifetimeManager 实际实现类型（告警项，不阻断）。
        // backplane 关闭时静默退化为进程内广播（DefaultHubLifetimeManager），单实例怎么测都对，
        // 只有多实例才暴露——部署多实例前确认这里不是 Default。
        var hubLifetimeManagerType = context.ServiceProvider
            .GetRequiredService<HubLifetimeManager<NotificationHub>>().GetType();
        context.ServiceProvider
            .GetRequiredService<ILogger<AbpAdminHttpApiHostModule>>()
            .LogInformation(
                "SignalR HubLifetimeManager<NotificationHub> = {HubLifetimeManagerType} (SignalR:UseRedisBackplane={UseRedisBackplane})",
                hubLifetimeManagerType.Name,
                configuration.GetValue<bool>("SignalR:UseRedisBackplane"));
    }

    /// <summary>
    /// T3.3 第 7 步：分布式锁启动探针 + 定时作业全量重注册。
    /// 用 OnPostApplicationInitializationAsync 而不是 OnApplicationInitialization：
    /// 需要 Quartz 的 IScheduler 已经启动（AbpBackgroundWorkersQuartzModule 在
    /// OnApplicationInitialization 阶段启动 scheduler，我们要排在它之后）。
    /// 探针与 ScheduleAllAsync 合在同一个方法里，先后关系才是明写的而不是依赖框架顺序。
    /// </summary>
    public override async Task OnPostApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // 探针纪律（00-overview 6.5「纪律之一（可观测）」的第一个落地点，形状四点）：
        // 打类型 + 对一个与业务锁不可能重名的固定键真拿一次锁，写进同一条日志。
        // 只打类型证明不了到 Redis 的锁链路真的通。
        // 探针必须排在 ScheduleAllAsync 前面：Redis 开着但连不上时，
        // 宁可在注册任何作业之前就失败，也不要留下一批"注册成功、但锁不跨进程"的作业。
        // 多实例同时启动时有实例拿不到探针锁属正常（说明锁真的跨进程生效），不算失败。
        var distributedLock = context.ServiceProvider.GetRequiredService<IAbpDistributedLock>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<AbpAdminHttpApiHostModule>>();

        var lockType = distributedLock.GetType().FullName;
        await using (var probe = await distributedLock.TryAcquireAsync(
                         "startup-probe", TimeSpan.Zero)) // 拿到即释放，只为证明链路真的通
        {
            logger.LogInformation(
                "IAbpDistributedLock implementation: {Type}, probe acquired: {Acquired}",
                lockType, probe != null);
        }

        // handler 委托存在进程内的 IDynamicBackgroundWorkerHandlerRegistry 里，而 Quartz job
        // 存在持久化库里——进程重启后 job 还在、handler 没了，adapter 只打一行
        // "No handler registered for dynamic worker" 警告然后静默返回（05 14.3 已核实）。
        // 所以每个实例启动时全量重注册是必需步骤，不是优化。
        // AddAsync 同名是替换语义（05 14.2），多实例各跑一遍是安全的。
        var scheduler = context.ServiceProvider.GetRequiredService<IScheduledJobScheduler>();
        await scheduler.ScheduleAllAsync();
    }
}
