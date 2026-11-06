using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class AdminErrorEligibilityResolverTests
{
    private readonly AdminErrorEligibilityResolver _resolver;

    public AdminErrorEligibilityResolverTests()
    {
        _resolver = new AdminErrorEligibilityResolver();
    }

    /// <summary>
    /// 验证：Resolve 拒绝 null context。
    /// 行为：抛出 ArgumentNullException。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Reject_Null_Context()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => _resolver.ResolveAsync(null!));
    }

    /// <summary>
    /// 验证：Resolve 拒绝 null HttpContext。
    /// 行为：返回 IsEligible=false，原因是 EligibilityQueryFailed。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_HttpContext_Null()
    {
        var context = new AdminErrorEligibilityContext
        {
            HttpContext = null,
            EndpointRegistration = new AdminErrorEndpointRegistration
            {
                ExitId = "TestController.Test()",
                DeclaringType = "TestController",
                MethodName = "Test",
                ParameterSignature = "()",
                Audience = ClientErrorAudience.AdminCompatibility,
                Protocol = ErrorProtocol.AbpEnvelope,
            },
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.EligibilityQueryFailed);
    }

    /// <summary>
    /// 验证：Resolve 拒绝无 EndpointRegistration。
    /// 行为：返回 IsEligible=false，原因是 NotRegistered。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_Registration_Null()
    {
        var context = new AdminErrorEligibilityContext
        {
            HttpContext = CreateHttpContext(),
            EndpointRegistration = null,
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.NotRegistered);
    }

    /// <summary>
    /// 验证：Resolve 拒绝非 AdminCompatibility audience。
    /// 行为：返回 IsEligible=false，原因是 NotDeclaredAdminCompatibility。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_Not_AdminCompatibility()
    {
        var context = new AdminErrorEligibilityContext
        {
            HttpContext = CreateHttpContext(),
            EndpointRegistration = new AdminErrorEndpointRegistration
            {
                ExitId = "TestController.Test()",
                DeclaringType = "TestController",
                MethodName = "Test",
                ParameterSignature = "()",
                Audience = ClientErrorAudience.Public,
                Protocol = ErrorProtocol.AbpEnvelope,
            },
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.NotDeclaredAdminCompatibility);
    }

    /// <summary>
    /// 验证：Resolve 拒绝未认证用户。
    /// 行为：返回 IsEligible=false，原因是 Unauthenticated。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_Unauthenticated()
    {
        var context = new AdminErrorEligibilityContext
        {
            HttpContext = CreateHttpContext(authenticated: false),
            EndpointRegistration = new AdminErrorEndpointRegistration
            {
                ExitId = "TestController.Test()",
                DeclaringType = "TestController",
                MethodName = "Test",
                ParameterSignature = "()",
                Audience = ClientErrorAudience.AdminCompatibility,
                Protocol = ErrorProtocol.AbpEnvelope,
            },
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.Unauthenticated);
    }

    /// <summary>
    /// 验证：Resolve 接受有效的 AdminCompatibility 请求。
    /// 行为：返回 IsEligible=true。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_True_When_Valid_Admin_Compatibility()
    {
        var context = new AdminErrorEligibilityContext
        {
            HttpContext = CreateHttpContext(authenticated: true),
            EndpointRegistration = new AdminErrorEndpointRegistration
            {
                ExitId = "TestController.Test()",
                DeclaringType = "TestController",
                MethodName = "Test",
                ParameterSignature = "()",
                Audience = ClientErrorAudience.AdminCompatibility,
                Protocol = ErrorProtocol.AbpEnvelope,
            },
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeTrue();
        result.DegradeReason.ShouldBeNull();
    }

    /// <summary>
    /// 验证：已登录但租户上下文未就绪时降级公开。
    /// 行为：返回 IsEligible=false，原因是 SessionOrTenantNotReady。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_Session_Or_Tenant_Not_Ready()
    {
        var httpContext = CreateHttpContext(authenticated: true);
        var services = new ServiceCollection();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.TenantId.Returns(Guid.NewGuid());
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsAvailable.Returns(false);
        services.AddSingleton(user);
        services.AddSingleton(tenant);
        httpContext.RequestServices = services.BuildServiceProvider();

        var context = new AdminErrorEligibilityContext
        {
            HttpContext = httpContext,
            EndpointRegistration = CreateRegistration(),
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.SessionOrTenantNotReady);
    }

    /// <summary>
    /// 验证：声明了权限但容器里没有 IPermissionChecker 时 fail-closed。
    /// 行为：返回 IsEligible=false，原因是 EligibilityQueryFailed。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_Permission_Checker_Missing()
    {
        var httpContext = CreateHttpContext(authenticated: true);
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();

        var context = new AdminErrorEligibilityContext
        {
            HttpContext = httpContext,
            EndpointRegistration = new AdminErrorEndpointRegistration
            {
                ExitId = "TestController.Test()",
                DeclaringType = "TestController",
                MethodName = "Test",
                ParameterSignature = "()",
                Audience = ClientErrorAudience.AdminCompatibility,
                Protocol = ErrorProtocol.AbpEnvelope,
                RequiredPermissions = ["Test.Read"],
            },
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.EligibilityQueryFailed);
    }

    /// <summary>
    /// 验证：已登录但缺少声明权限时降级公开。
    /// 行为：返回 IsEligible=false，原因是 PermissionDenied。
    /// </summary>
    [Fact]
    public async Task Resolve_Should_Return_False_When_Permission_Denied()
    {
        var httpContext = CreateHttpContext(authenticated: true);
        var services = new ServiceCollection();
        services.AddSingleton<IPermissionChecker>(new DenyAllPermissionChecker());
        httpContext.RequestServices = services.BuildServiceProvider();

        var context = new AdminErrorEligibilityContext
        {
            HttpContext = httpContext,
            EndpointRegistration = new AdminErrorEndpointRegistration
            {
                ExitId = "TestController.Test()",
                DeclaringType = "TestController",
                MethodName = "Test",
                ParameterSignature = "()",
                Audience = ClientErrorAudience.AdminCompatibility,
                Protocol = ErrorProtocol.AbpEnvelope,
                RequiredPermissions = ["Test.Read"],
            },
        };

        var result = await _resolver.ResolveAsync(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.PermissionDenied);
    }

    private static AdminErrorEndpointRegistration CreateRegistration()
    {
        return new AdminErrorEndpointRegistration
        {
            ExitId = "TestController.Test()",
            DeclaringType = "TestController",
            MethodName = "Test",
            ParameterSignature = "()",
            Audience = ClientErrorAudience.AdminCompatibility,
            Protocol = ErrorProtocol.AbpEnvelope,
        };
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated = true)
    {
        var httpContext = new DefaultHttpContext();
        if (authenticated)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-id"),
            }, "Test"));
        }
        return httpContext;
    }

    private sealed class DenyAllPermissionChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string name) => Task.FromResult(false);

        public Task<bool> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string name) => Task.FromResult(false);

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(params string[] names)
            => throw new NotSupportedException();

        public Task<MultiplePermissionGrantResult> IsGrantedAsync(
            ClaimsPrincipal? claimsPrincipal,
            params string[] names)
            => throw new NotSupportedException();
    }
}

public class ClientErrorProtocolAdapterTests
{
    /// <summary>
    /// 验证：OAuthErrorProtocolAdapter 协议类型。
    /// 行为：Protocol 属性返回 OAuth。
    /// </summary>
    [Fact]
    public void OAuth_Adapter_Should_Have_Correct_Protocol()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        adapter.Protocol.ShouldBe(ErrorProtocol.OAuth);
    }

    /// <summary>
    /// 验证：AbpEnvelopeProtocolAdapter 协议类型。
    /// 行为：Protocol 属性返回 AbpEnvelope。
    /// </summary>
    [Fact]
    public void Abp_Adapter_Should_Have_Correct_Protocol()
    {
        var adapter = new AbpEnvelopeProtocolAdapter();
        adapter.Protocol.ShouldBe(ErrorProtocol.AbpEnvelope);
    }

    /// <summary>
    /// 验证：SignalRProtocolAdapter 协议类型。
    /// 行为：Protocol 属性返回 SignalR。
    /// </summary>
    [Fact]
    public void SignalR_Adapter_Should_Have_Correct_Protocol()
    {
        var adapter = new SignalRProtocolAdapter();
        adapter.Protocol.ShouldBe(ErrorProtocol.SignalR);
    }

    /// <summary>
    /// 验证：OAuthErrorProtocolAdapter 拒绝 null HttpContext。
    /// 行为：抛出 ArgumentNullException (wrapped in AggregateException for sync call)。
    /// </summary>
    [Fact]
    public void OAuth_Adapter_Should_Reject_Null_HttpContext()
    {
        var adapter = new OAuthErrorProtocolAdapter();

        var ex = Should.Throw<AggregateException>(() =>
            adapter.TryWriteProtocolErrorAsync(null!, CreateProjection(), default).Wait());

        ex.InnerException.ShouldBeOfType<ArgumentNullException>();
    }

    /// <summary>
    /// 验证：OAuthErrorProtocolAdapter 拒绝 null projection。
    /// 行为：抛出 ArgumentNullException (wrapped in AggregateException for sync call)。
    /// </summary>
    [Fact]
    public void OAuth_Adapter_Should_Reject_Null_Projection()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        var httpContext = new DefaultHttpContext();

        var ex = Should.Throw<AggregateException>(() =>
            adapter.TryWriteProtocolErrorAsync(httpContext, null!, default).Wait());

        ex.InnerException.ShouldBeOfType<ArgumentNullException>();
    }

    /// <summary>
    /// 验证：OAuthErrorProtocolAdapter 本身不按路径拒绝。生产 /api 不再把请求交给它。
    /// </summary>
    [Fact]
    public async Task OAuth_Adapter_Should_Write_For_Api_Path_When_Invoked()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/test";

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, CreateProjection());

        result.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(500);
    }

    /// <summary>
    /// 验证：OAuthErrorProtocolAdapter 处理 /connect 路径。
    /// 行为：返回 true 并写入响应。
    /// </summary>
    [Fact]
    public async Task OAuth_Adapter_Should_Handle_Connect_Path()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/connect/authorize";

        var projection = new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.AuthenticationRequired,
                Kind = PublicErrorKind.Authentication,
                Message = "Test message",
            },
            StatusCode = 401,
        };

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, projection);

        result.ShouldBeTrue();
    }

    /// <summary>
    /// 验证：OAuthErrorProtocolAdapter 映射错误类型。
    /// 行为：Authentication kind → access_denied。
    /// </summary>
    [Fact]
    public async Task OAuth_Adapter_Should_Map_Authentication_Kind()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/connect/token";

        var projection = new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.AuthenticationRequired,
                Kind = PublicErrorKind.Authentication,
                Message = "Test",
            },
            StatusCode = 401,
        };

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, projection);

        result.ShouldBeTrue();
        httpContext.Response.Headers.ContainsKey("WWW-Authenticate").ShouldBeTrue();
    }

    /// <summary>
    /// Clear 之后必须按投影重写封顶 Retry-After，否则 /connect/token 429 丢掉等待秒数。
    /// </summary>
    [Fact]
    public async Task OAuth_Adapter_Should_Write_Capped_Retry_After()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Path = "/connect/token";

        var projection = new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.RateLimited,
                Kind = PublicErrorKind.RateLimited,
                Message = "slow",
            },
            StatusCode = 429,
            RetryAfterSeconds = 90000,
        };

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, projection);

        result.ShouldBeTrue();
        httpContext.Response.Headers.RetryAfter.ToString().ShouldBe("86400");
    }

    /// <summary>
    /// 验证：AbpEnvelopeProtocolAdapter 总是返回 false。
    /// 行为：不处理任何请求（默认适配器）。
    /// </summary>
    [Fact]
    public async Task Abp_Adapter_Should_Always_Return_False()
    {
        var adapter = new AbpEnvelopeProtocolAdapter();
        var httpContext = new DefaultHttpContext();

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, CreateProjection());

        result.ShouldBeFalse();
    }

    /// <summary>
    /// 验证：SignalRProtocolAdapter 未开始响应时写状态且不套 ABP 信封。
    /// </summary>
    [Fact]
    public async Task SignalR_Adapter_Should_Set_Status_Without_Envelope()
    {
        var adapter = new SignalRProtocolAdapter();
        var httpContext = new DefaultHttpContext();

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, CreateProjection());

        result.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(500);
    }

    private static ClientErrorHttpProjection CreateProjection()
    {
        return new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.Unexpected,
                Kind = PublicErrorKind.Server,
                Message = "Test error",
            },
            StatusCode = 500,
        };
    }
}