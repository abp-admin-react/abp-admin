using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.Authorization;

/// <summary>
/// 匿名可达端点全量审计（模块五收口的全局化）：把 SettingUi 裸奔事件的教训从
/// "逐端点补丁"升级为"系统属性"——任何端点要么带授权元数据（[Authorize]），
/// 要么被宿主 FallbackPolicy（默认拒绝）兜住；[AllowAnonymous] 只允许出现在
/// 本测试显式声明的白名单里，新增匿名端点不登记即红。
/// </summary>
/// <remarks>
/// <para>
/// 这不是 HTTP 层的请求回放（宿主级 TestServer 需要 Redis/DB 齐活，本仓库测试纪律
/// 刻意不启动完整宿主——见 HostInitializationLogTests 的说明）。这里把宿主真的初始化
/// 到端点表构建完成（InitializeApplicationAsync），然后对 AuthorizationMiddleware 的
/// 判定输入做静态断言：该中间件的行为由源码钉死（2026-09 核实）——
/// 无 IAllowAnonymous 时，元数据里有 IAuthorizeData 就按其组合策略，
/// 没有就取 FallbackPolicy。因此"端点表 + FallbackPolicy 配置"与中间件的实际放行
/// 判定是同构的，静态断言覆盖的就是真实行为。
/// </remarks>
/// <para>
/// <b>双向断言</b>：只有"已匿名必登记"是不够的——OCR 评审实证过反例（10.6.1 的
/// end-session 控制器类名是 LogoutController，按 GitHub dev 分支写的 EndSessionController
/// 匹配了个不存在的类，约定静默失配、全套测试照样绿、SPA 登出炸掉）。所以
/// <see cref="Login_Flow_Endpoints_Must_Actually_Be_Anonymous"/> 反向钉死登录链路端点
/// 必须真的携带匿名元数据。
/// </para>
/// <para>
/// 宿主进程内静态共享且从不 Dispose：Quartz 的 LogProvider 把 LoggerFactory 缓存在
/// 静态字段里，第一个实例 DisposeAsync 之后任何再初始化都会撞
/// ObjectDisposedException——"每测试方法一个宿主"在这个宿主上不可行。测试进程
/// 结束即回收，泄漏是刻意的、可接受的。若未来第二个测试类也要初始化完整宿主，
/// 应把本类的 LazyHost 提升为公共基建，而不是再复制一份。
/// </para>
/// <para>
/// 数据库隔离：宿主初始化有真实写库副作用（静态模板同步、后台作业注册），直接指
/// 仓库根的 dev 库会与运行中的后端抢 SQLite 文件锁——这里把 dev 库复制一份临时文件
/// 再把连接串覆盖过去，副作用全部落在副本上（ABP 初始化需要已迁移的表结构，空库起不来）。
/// </para>
/// </remarks>
public class AnonymousEndpointSweepTests
{
    private static readonly Lazy<Task<WebApplication>> LazyHost = new(() => CreateInitializedHostAsync());

    private static async Task<WebApplication> CreateInitializedHostAsync()
    {
        // A-block 迁移机制重构后宿主是 PostgreSQL-only（EF 模块无条件 UseNpgsql，
        // SQLite 分支已删）——SQLite 文件副本隔离不再可行。改为：凭证沿用宿主同一分层源
        // （appsettings.json → appsettings.secrets.json，与 Program.cs 同序），schema 隔离到
        // 专用 abp_admin_hosttest（不存在则创建；宿主启动的 AutoMigrateOnStartup 会在其中
        // 自举迁移+种子），与 dev schema（abp_admin_efm）互不干扰。
        var baseConn = ResolveHostDatabaseConnectionString();
        var connString = await EnsureIsolatedSchemaAsync(baseConn, "abp_admin_hosttest");

        var builder = await HostUnderTest.CreateBuilderAsync(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connString,
            },
            useAutofac: true);
        var app = builder.Build();
        await app.InitializeApplicationAsync();
        return app;
    }

    private static Task<WebApplication> HostTask => LazyHost.Value;

    /// <summary>
    /// 沿用宿主同序的连接串分层（appsettings.json → appsettings.secrets.json；环境变量与
    /// in-memory 源在 HostUnderTest 内已分层）。测试进程不依赖用户机器的环境变量注入。
    /// </summary>
    private static string ResolveHostDatabaseConnectionString()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AbpAdmin.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("AbpAdmin.slnx not found above the test binaries.");
        }

        var hostRoot = Path.Combine(directory.FullName, "src", "AbpAdmin.HttpApi.Host");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(hostRoot, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(hostRoot, "appsettings.secrets.json"), optional: true)
            .Build();

        var conn = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(conn) || conn.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "匿名端点扫查需要 PostgreSQL 连接串（宿主已 PG-only）。" +
                "请在 src/AbpAdmin.HttpApi.Host/appsettings.secrets.json 配置 ConnectionStrings:Default（含凭据）。");
        }

        return conn;
    }

    /// <summary>确保专用 schema 存在并返回追加 SearchPath 的连接串（测试写副作用全部落在该 schema）。</summary>
    private static async Task<string> EnsureIsolatedSchemaAsync(string connectionString, string schemaName)
    {
        var bare = connectionString.Contains("SearchPath=", StringComparison.OrdinalIgnoreCase)
            ? connectionString[..connectionString.IndexOf("SearchPath=", StringComparison.OrdinalIgnoreCase)].TrimEnd(';')
            : connectionString;

        await using var conn = new NpgsqlConnection(bare);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"CREATE SCHEMA IF NOT EXISTS {schemaName}", conn);
        await cmd.ExecuteNonQueryAsync();

        return bare.EndsWith(";") ? bare + $"SearchPath={schemaName}" : bare + $";SearchPath={schemaName}";
    }

    /// <summary>一个匿名端点的判定面：路由模板 + 显示名（控制器动作/页面的全名）。</summary>
    private sealed record EndpointInfo(string Template, string DisplayName);

    /// <summary>与 PreLoginRazorPagesConvention.AnonymousPageRoutes 同源的页面路径清单。</summary>
    /// （声明须先于 AllowedAnonymousAreas：后者初始化器里的 lambda 引用本字段，
    /// 声明顺序靠后会被编译器流分析判为"可能未初始化"（CS8602）。）
    private static readonly HashSet<string> PreLoginPages =
    [
        "Account/Login", "Account/LoginWith2fa", "Account/LoginWithRecoveryCode",
        "Account/TwoFactorVerification", "Account/Lockout",
        "Account/ForgotPassword", "Account/ForgotPasswordConfirmation",
        "Account/Register", "Account/RegisterConfirmation", "Account/ConfirmEmail",
        "Account/LinkLogin", "Account/LinkLoginCallback",
        "Account/Logout", "Account/LoggedOut", "Account/AccessDenied",
    ];

    /// <summary>
    /// 显式匿名白名单。每条都要写清"为什么它匿名是安全的"——
    /// 白名单不是法外之地，是逐条决策的台账。匹配一律收窄到端点身份
    /// （精确模板 / 精确类型前缀 / 非控制器端点），不用"长得像"的宽匹配。
    /// </summary>
    private static readonly IReadOnlyList<AllowedAnonymousArea> AllowedAnonymousAreas =
    [
        new(
            "OpenIddict 协议端点（登录/授权/登出自身；由 OpenIddictConnectEndpointsConvention 显式豁免，"
            + "路由由上游控制器 [Route] 钉死，模板恒定；authorize/callback 是外链登录回跳，同属登录链路）",
            e => e.Template is "connect/token" or "connect/authorize" or "connect/authorize/callback"
                 or "connect/endsession"),

        new(
            "健康检查探针与面板（k8s/lb 探活不带凭据；NoExceptionDetails 已防异常回显；"
            + "webhooks/ui-settings 是面板自身的数据面，绑定本机收集端点）",
            e => e.Template.StartsWith("health-", StringComparison.OrdinalIgnoreCase)
                 || e.Template is "healthchecks-webhooks"
                 || e.Template.StartsWith("ui/resources/", StringComparison.OrdinalIgnoreCase)),

        new(
            "ABP application-configuration / application-localization（前端登录页渲染需要，"
            + "由 PreLoginApiEndpointsConvention 豁免——10.6.1 控制器无授权标注，此前只是事实上匿名）",
            e => e.Template is "api/abp/application-configuration" or "api/abp/application-localization"),

        new(
            "Account 登录链路 Razor Pages（登录/找回密码/注册确认/2FA/登出——认证发生之前就必须可达；"
            + "由 PreLoginRazorPagesConvention 按 Precise 页面路径豁免，与该约定的列表逐条对应；"
            + "/Account/Manage 有显式授权，绝不在此列）",
            e => e.Template.StartsWith("Account/", StringComparison.OrdinalIgnoreCase)
                 && PreLoginPages.Contains(e.Template)),

        new(
            "wwwroot 静态资源（前端脚本/图片/样式，不含运行时数据；MapAbpStaticAssets 显式豁免，"
            + "含 .gz 预压缩产物。只认【无 DisplayName 的静态资源端点】——控制器动作绝不会以空显示名出现，"
            + "防止未来某个 /api/.../x.txt 接口蹭后缀匹配混进匿名面）",
            e => string.IsNullOrEmpty(e.DisplayName)
                 && (e.Template.StartsWith("libs/", StringComparison.OrdinalIgnoreCase)
                     || e.Template.StartsWith("images/", StringComparison.OrdinalIgnoreCase)
                     || HasStaticAssetExtension(e.Template))),

        new(
            "登录/注册前置流程（验证码、passkey 断言选项、邮箱/手机确认码、无密码登录、"
            + "自助注册开关查询）——认证发生之前就必须可达，属登录链路的一部分",
            e => e.Template is "api/app/captcha-image"
                     or "api/app/account-passkey/assertion-options"
                     or "api/app/account-pro/is-self-registration-enabled"
                     or "api/app/account-pro/send-email-confirmation-code"
                     or "api/app/account-pro/confirm-email"
                     or "api/app/account-pro/send-phone-number-confirmation-code"
                     or "api/app/account-pro/confirm-phone-number"
                     or "api/app/account-pro/send-passwordless-login-code"
                     or "api/app/account-pro/login-with-magic-link"),

        new(
            "Cookie 同意（未登录访客在登录页之前点同意，天然匿名）",
            e => e.Template is "api/app/cookie-consent/accept"),

        new(
            "令牌即凭据的下载（file-share 分享链接 / GDPR 个人数据包：一次性/不可猜 token "
            + "本身就是授权，与登录态无关。按承载类型的精确全名前缀匹配——这些动作同时挂在"
            + "常规控制器与 {controller=Home} 兜底路由两种形态上，按模板放行会放开整类路由）",
            e => e.DisplayName.StartsWith("AbpAdmin.Controllers.FileShareController.DownloadByTokenAsync", StringComparison.Ordinal)
                 || e.DisplayName.StartsWith("AbpAdmin.Controllers.GdprDownloadController.DownloadAsync", StringComparison.Ordinal)
                 || e.DisplayName.StartsWith("AbpAdmin.Files.FileShareAppService.DownloadByTokenAsync", StringComparison.Ordinal)
                 || e.DisplayName.StartsWith("AbpAdmin.Gdpr.GdprRequestAppService.DownloadAsync", StringComparison.Ordinal)),
    ];

    /// <summary>
    /// 登录链路必须匿名可达的端点（反向断言）：
    /// connect 三件套（token=登录、authorize=授权码流程、endsession=SPA 登出）+
    /// Razor 登录页 + application-configuration/application-localization（前端登录页拉基础
    /// 配置与本地化；Chrome 实测抓到过开默认拒绝后它们 401——上游 10.6.1 控制器根本没有
    /// [AllowAnonymous]，此前只是"事实上匿名"）。
    /// 约定失配（ABP 升级改类名/页面路径）时这里先红，而不是等登录/登出在线上炸了才发现。
    /// </summary>
    private static readonly string[] MustBeAnonymousTemplates =
    [
        "connect/token",
        "connect/authorize",
        "connect/endsession",
        "Account/Login",
        "api/abp/application-configuration",
        "api/abp/application-localization",
    ];

    [Fact]
    public async Task FallbackPolicy_Should_Require_Authenticated_User()
    {
        var app = await HostTask;
        var options = app.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        var fallback = options.FallbackPolicy;
        fallback.ShouldNotBeNull(
            "宿主必须配置 FallbackPolicy（默认拒绝），否则任何缺授权元数据的端点（含未来模块升级引入的）"
            + "都会退回匿名可达——SettingUi 裸奔事件正是这个形状。");

        fallback.Requirements
            .ShouldContain(r => r is DenyAnonymousAuthorizationRequirement,
                "FallbackPolicy 必须至少要求已认证（RequireAuthenticatedUser）。");
    }

    [Fact]
    public async Task Every_Anonymous_Endpoint_Must_Be_Declared_In_The_Allowlist()
    {
        var app = await HostTask;

        var offenders = new List<string>();

        foreach (var endpoint in AllRouteEndpoints(app))
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() == null)
            {
                continue;
            }

            var info = new EndpointInfo(TemplateOf(endpoint), endpoint.DisplayName ?? string.Empty);
            if (AllowedAnonymousAreas.All(area => !area.Matches(info)))
            {
                offenders.Add($"{info.Template}  [{info.DisplayName}]");
            }
        }

        offenders.ShouldBeEmpty(
            "以下端点声明了匿名可达（IAllowAnonymous）但不在白名单：\n" + string.Join("\n", offenders) +
            "\n\n要么是新增的匿名端点未登记（在 AllowedAnonymousAreas 补条目并写明理由），" +
            "要么是有人误加了 [AllowAnonymous]（删掉它）。");
    }

    [Fact]
    public async Task Login_Flow_Endpoints_Must_Actually_Be_Anonymous()
    {
        var app = await HostTask;
        var byTemplate = AllRouteEndpoints(app)
            .GroupBy(TemplateOf)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var broken = new List<string>();

        foreach (var template in MustBeAnonymousTemplates)
        {
            if (!byTemplate.TryGetValue(template, out var endpoints))
            {
                broken.Add($"{template}：端点表中不存在（路由消失或改路径）");
                continue;
            }

            if (endpoints.All(e => e.Metadata.GetMetadata<IAllowAnonymous>() == null))
            {
                var filter = endpoints
                    .SelectMany(e => e.Metadata)
                    .FirstOrDefault(m => m.GetType().Name == "AllowAnonymousFilter");
                var interfaces = filter != null
                    ? string.Join(", ", filter.GetType().GetInterfaces().Select(i => i.Name))
                    : "(no filter found)";
                broken.Add(
                    $"{template}：存在但没有匿名元数据。AllowAnonymousFilter 接口集：[{interfaces}]");
            }
        }

        broken.ShouldBeEmpty(
            "登录链路端点失去匿名可达性，登录/登出会直接被默认拒绝拦断：\n" + string.Join("\n", broken));
    }

    [Fact]
    public async Task Endpoint_Table_Should_Be_Initialized_Not_Vacuously_Empty()
    {
        var app = await HostTask;
        var templates = AllRouteEndpoints(app).Select(TemplateOf).ToHashSet();

        // 防空洞通过：初始化没跑起来时端点表为空，上面几条测试会假绿。
        // /health-status 由 ConfigureHealthChecks 经 AbpEndpointRouterOptions 在模块初始化阶段挂载，
        // 它存在即证明初始化真的执行到了端点构建。
        templates.Count.ShouldBeGreaterThan(200,
            $"端点表只有 {templates.Count} 条，宿主初始化大概率没跑完整（ABP 宿主通常数百条）。");

        templates.ShouldContain("health-status",
            "/health-status 不在端点表里：健康检查端点由模块初始化挂载，缺了它说明本测试的初始化不完整。");
    }

    private static IEnumerable<RouteEndpoint> AllRouteEndpoints(WebApplication app)
    {
        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();
        return dataSource.Endpoints.OfType<RouteEndpoint>();
    }

    private static string TemplateOf(RouteEndpoint endpoint)
    {
        return endpoint.RoutePattern.RawText?.TrimStart('/') ?? string.Empty;
    }

    private static bool HasStaticAssetExtension(string template)
    {
        return template.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".js.gz", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".css.gz", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
               || template.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AllowedAnonymousArea(string Reason, Func<EndpointInfo, bool> Matches);
}
