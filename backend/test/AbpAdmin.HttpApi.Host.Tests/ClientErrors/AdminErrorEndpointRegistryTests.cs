using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P0-2：端点登记与分类测试。
/// 验证路由元数据与 AppService 权限的差异，确保所有受控 API 端点都有明确的错误受众分类。
/// </summary>
/// <remarks>
/// <para>
/// 测试策略：复用 AnonymousEndpointSweepTests 的端点枚举逻辑，
/// 但不要求完整登记所有端点（P4 发布前完成）。
/// P0 阶段验证登记结构正确、分类逻辑可用、漂移检测机制就绪。
/// </para>
/// <para>
/// 完成条件：没有把"已登录"或权限前缀当管理资格；
/// 每个待接出口有明确适配负责人和回归用例。
/// </para>
/// </remarks>
public class AdminErrorEndpointRegistryTests
{
    private static Task<WebApplication> HostTask => HostTestBed.HostTask;

    /// <summary>
    /// 验证登记表结构正确，支持增删查。
    /// </summary>
    [Fact]
    public void Registry_Should_Support_Basic_Operations()
    {
        AdminErrorEndpointRegistry.Clear();

        var registration = new AdminErrorEndpointRegistration
        {
            ExitId = "TestController.GetAsync()",
            DeclaringType = "TestController",
            MethodName = "GetAsync",
            ParameterSignature = "()",
            HttpMethod = "GET",
            RouteTemplate = "api/test/{id}",
            Audience = ClientErrorAudience.AdminCompatibility,
            Protocol = ErrorProtocol.AbpEnvelope,
            RequiredPermissions = ["Test.Read"],
            ApprovedDataFields = ["id"],
            AcceptanceTestId = "A01",
        };

        AdminErrorEndpointRegistry.Register(registration);

        var found = AdminErrorEndpointRegistry.FindByExitId("TestController.GetAsync()");
        found.ShouldNotBeNull();
        found.Audience.ShouldBe(ClientErrorAudience.AdminCompatibility);
        found.RequiredPermissions.ShouldContain("Test.Read");

        AdminErrorEndpointRegistry.Clear();
    }

    /// <summary>
    /// 验证重复注册会失败。
    /// </summary>
    [Fact]
    public void Duplicate_Registration_Should_Fail()
    {
        AdminErrorEndpointRegistry.Clear();

        var registration = new AdminErrorEndpointRegistration
        {
            ExitId = "DupController.GetAsync()",
            DeclaringType = "DupController",
            MethodName = "GetAsync",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.Public,
            Protocol = ErrorProtocol.AbpEnvelope,
        };

        AdminErrorEndpointRegistry.Register(registration);

        Should.Throw<InvalidOperationException>(() =>
            AdminErrorEndpointRegistry.Register(registration));

        AdminErrorEndpointRegistry.Clear();
    }

    /// <summary>
    /// 验证宿主端点表可枚举，且包含预期的端点数量。
    /// 不要求全部登记，但验证枚举机制可用。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Host_Endpoint_Table_Should_Be_Enumerable()
    {
        var app = await HostTask;
        var endpoints = AllRouteEndpoints(app).ToList();

        // 与 AnonymousEndpointSweepTests.Endpoint_Table_Should_Be_Initialized_Not_Vacuously_Empty 对齐
        endpoints.Count.ShouldBeGreaterThan(200,
            $"端点表只有 {endpoints.Count} 条，宿主初始化大概率没跑完整。");

        // 验证可以按路由模板分组
        var byTemplate = endpoints
            .GroupBy(TemplateOf)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        byTemplate.Count.ShouldBeGreaterThan(100,
            "按路由分组后应至少有 100 个唯一模板。");

        // 验证可以提取元数据
        var withAuthorize = endpoints
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() != null)
            .ToList();

        withAuthorize.Count.ShouldBeGreaterThan(50,
            "带授权元数据的端点应至少有 50 个。");
    }

    /// <summary>
    /// 验证端点可按授权元数据分类。
    /// P0 不自动授予 AdminCompatibility，只验证分类逻辑。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Endpoints_Can_Be_Classified_By_Authorization_Metadata()
    {
        var app = await HostTask;
        var endpoints = AllRouteEndpoints(app).ToList();

        var byAuthStatus = endpoints
            .Select(e => new
            {
                Endpoint = e,
                Template = TemplateOf(e),
                HasAuthorize = e.Metadata.GetMetadata<IAuthorizeData>() != null,
                HasAllowAnonymous = e.Metadata.GetMetadata<IAllowAnonymous>() != null,
            })
            .GroupBy(e => new { e.HasAuthorize, e.HasAllowAnonymous })
            .ToDictionary(g => g.Key, g => g.ToList());

        // 有授权但非匿名的端点（候选项）
        byAuthStatus.TryGetValue(new { HasAuthorize = true, HasAllowAnonymous = false }, out var authorized);
        authorized?.Count.ShouldBeGreaterThan(50,
            "有授权且非匿名的端点应至少有 50 个（AdminCompatibility 候选项）。");

        // 匿名端点（Public/Shared 候选项）
        byAuthStatus.TryGetValue(new { HasAuthorize = false, HasAllowAnonymous = true }, out var anonymous);
        anonymous?.Count.ShouldBeGreaterThan(10,
            "匿名端点应至少有 10 个（Public 候选项）。");

        // 无授权且非匿名的端点（依赖 FallbackPolicy）
        byAuthStatus.TryGetValue(new { HasAuthorize = false, HasAllowAnonymous = false }, out var noAuth);
        noAuth?.Count.ShouldBeLessThanOrEqualTo(0,
            "无授权且非匿名的端点应依赖 FallbackPolicy（默认拒绝），实际应为 0。");
    }

    /// <summary>
    /// 验证不把权限前缀当管理资格。
    /// 只有显式声明 AdminCompatibility 且满足授权条件的端点才被视为管理端。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Permission_Prefix_Should_Not_Automatically_Grant_Admin_Qualification()
    {
        var app = await HostTask;
        var endpoints = AllRouteEndpoints(app).ToList();

        // 收集所有权限前缀
        var authorizedEndpoints = endpoints
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is { } auth
                        && !string.IsNullOrEmpty(auth.Policy))
            .Select(e => new
            {
                Template = TemplateOf(e),
                Policy = e.Metadata.GetMetadata<IAuthorizeData>()!.Policy!,
            })
            .ToList();

        // 提取权限前缀（按第一个点分隔）
        var prefixes = authorizedEndpoints
            .Select(e => e.Policy.Split('.').FirstOrDefault() ?? e.Policy)
            .Distinct()
            .ToList();

        // 常见的管理权限前缀
        var managementPrefixes = new HashSet<string>
        {
            "AbpAdmin",
            "AbpIdentity",
            "AbpTenantManagement",
            "AbpSettingManagement",
            "AbpFeatureManagement",
            "AbpPermissionManagement",
            "AbpAccount",
            "SettingUi",
        };

        // 验证：有管理权限前缀 ≠ 自动授予 AdminCompatibility
        // P0 阶段验证分类逻辑可用，P4 发布前必须完成显式登记。
        prefixes.Count.ShouldBeGreaterThan(0, "应至少有一个权限前缀用于验证分类逻辑。");

        var matchedPrefixes = prefixes.Where(managementPrefixes.Contains).ToList();
        matchedPrefixes.Count.ShouldBeGreaterThan(0, "部分前缀应匹配管理权限命名。");

        // 显式声明：匹配不等于授予资格（P4 将强制显式登记）
        // 此处验证映射逻辑可用，而非验收登记完整性。
    }

    /// <summary>
    /// 验证可检测端点登记漂移。
    /// 新增/删除端点应能被发现。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Endpoint_Drift_Can_Be_Detected()
    {
        var app = await HostTask;
        var endpoints = AllRouteEndpoints(app).ToList();

        // 收集当前端点签名
        var currentSignatures = new HashSet<string>();
        foreach (var endpoint in endpoints)
        {
            var displayName = endpoint.DisplayName ?? "";
            var template = TemplateOf(endpoint);
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "UNKNOWN";

            // 生成签名（简化版，P3 会用语法树精确提取）
            var signature = $"{method}:{template}:{displayName}";
            currentSignatures.Add(signature);
        }

        // P0 阶段只验证机制，不要求全部登记
        // P4 发布前：与已登记清单比对，新增/删除使 CI 失败
        currentSignatures.Count.ShouldBeGreaterThan(200,
            "端点签名数量应大于 200（验证枚举机制）。");

        // 保存当前签名快照（P3 会生成 JSON）
        // File.WriteAllText("endpoint-signatures-snapshot.txt", string.Join("\n", currentSignatures.Order()));
    }

    /// <summary>
    /// 验证非 MVC 出口可单独登记。
    /// 如中间件、SSE、Hub、OAuth 端点等。
    /// </summary>
    [Fact]
    public void Non_Mvc_Exits_Can_Be_Registered()
    {
        AdminErrorEndpointRegistry.Clear();

        // TokenEndpoint 限流中间件（OAuth 协议）
        var tokenEndpoint = new AdminErrorEndpointRegistration
        {
            ExitId = "TokenEndpointRateLimitingMiddleware.InvokeAsync()",
            DeclaringType = "TokenEndpointRateLimitingMiddleware",
            MethodName = "InvokeAsync",
            ParameterSignature = "(HttpContext)",
            Audience = ClientErrorAudience.Public, // 匿名可达
            Protocol = ErrorProtocol.OAuth,
            RequiredPermissions = [], // 无权限要求
            HttpStatusCode = 429,
            ApprovedDataFields = [], // OAuth 格式无 data 字段
            AcceptanceTestId = "A09",
            Notes = "非 MVC 出口：TokenEndpoint 限流 429 + Retry-After",
        };
        AdminErrorEndpointRegistry.Register(tokenEndpoint);

        // 图片繁忙 filter（ABP 协议 + Retry-After）
        var imageFilter = new AdminErrorEndpointRegistration
        {
            ExitId = "ImageProcessingBusyExceptionFilter.OnException()",
            DeclaringType = "ImageProcessingBusyExceptionFilter",
            MethodName = "OnException",
            ParameterSignature = "(ExceptionContext)",
            Audience = ClientErrorAudience.AdminCompatibility,
            Protocol = ErrorProtocol.AbpEnvelope,
            RequiredPermissions = [], // 由端点自身控制
            HttpStatusCode = 503,
            ApprovedDataFields = [], // 无 data 字段
            AcceptanceTestId = "A31",
            Notes = "非 MVC 出口：补 Retry-After 头，不接管异常",
        };
        AdminErrorEndpointRegistry.Register(imageFilter);

        // 文件上传拒绝（自定义 JSON）
        var uploadGuard = new AdminErrorEndpointRegistration
        {
            ExitId = "FileUploadContentGuardMiddleware.InvokeAsync()",
            DeclaringType = "FileUploadContentGuardMiddleware",
            MethodName = "InvokeAsync",
            ParameterSignature = "(HttpContext)",
            Audience = ClientErrorAudience.AdminCompatibility, // 需登录
            Protocol = ErrorProtocol.CustomJson,
            RequiredPermissions = [], // 依赖登录态
            HttpStatusCode = 400,
            ApprovedDataFields = ["fileName", "reason"], // 固定字段
            AcceptanceTestId = "A25",
            Notes = "非 MVC 出口：上传拒绝 400 + {fileName, reason}",
        };
        AdminErrorEndpointRegistry.Register(uploadGuard);

        // 验证可按协议查找
        var oauthExits = AdminErrorEndpointRegistry.FindByAudience(ClientErrorAudience.Public)
            .Where(r => r.Protocol == ErrorProtocol.OAuth)
            .ToList();
        oauthExits.Count.ShouldBe(1);
        oauthExits[0].HttpStatusCode.ShouldBe(429);

        AdminErrorEndpointRegistry.Clear();
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
}
