using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Http;
using Shouldly;
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
    public void Resolve_Should_Reject_Null_Context()
    {
        Should.Throw<ArgumentNullException>(() => _resolver.Resolve(null!));
    }

    /// <summary>
    /// 验证：Resolve 拒绝 null HttpContext。
    /// 行为：返回 IsEligible=false，原因是 EligibilityQueryFailed。
    /// </summary>
    [Fact]
    public void Resolve_Should_Return_False_When_HttpContext_Null()
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

        var result = _resolver.Resolve(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.EligibilityQueryFailed);
    }

    /// <summary>
    /// 验证：Resolve 拒绝无 EndpointRegistration。
    /// 行为：返回 IsEligible=false，原因是 NotRegistered。
    /// </summary>
    [Fact]
    public void Resolve_Should_Return_False_When_Registration_Null()
    {
        var context = new AdminErrorEligibilityContext
        {
            HttpContext = CreateHttpContext(),
            EndpointRegistration = null,
        };

        var result = _resolver.Resolve(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.NotRegistered);
    }

    /// <summary>
    /// 验证：Resolve 拒绝非 AdminCompatibility audience。
    /// 行为：返回 IsEligible=false，原因是 NotDeclaredAdminCompatibility。
    /// </summary>
    [Fact]
    public void Resolve_Should_Return_False_When_Not_AdminCompatibility()
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

        var result = _resolver.Resolve(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.NotDeclaredAdminCompatibility);
    }

    /// <summary>
    /// 验证：Resolve 拒绝未认证用户。
    /// 行为：返回 IsEligible=false，原因是 Unauthenticated。
    /// </summary>
    [Fact]
    public void Resolve_Should_Return_False_When_Unauthenticated()
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

        var result = _resolver.Resolve(context);

        result.IsEligible.ShouldBeFalse();
        result.DegradeReason.ShouldBe(AdminEligibilityDegradeReason.Unauthenticated);
    }

    /// <summary>
    /// 验证：Resolve 接受有效的 AdminCompatibility 请求。
    /// 行为：返回 IsEligible=true。
    /// </summary>
    [Fact]
    public void Resolve_Should_Return_True_When_Valid_Admin_Compatibility()
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

        var result = _resolver.Resolve(context);

        result.IsEligible.ShouldBeTrue();
        result.DegradeReason.ShouldBeNull();
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
    /// 验证：OAuthErrorProtocolAdapter 非 /connect 路径返回 false。
    /// 行为：不处理非 OAuth 端点。
    /// </summary>
    [Fact]
    public async Task OAuth_Adapter_Should_Return_False_For_Non_Connect_Path()
    {
        var adapter = new OAuthErrorProtocolAdapter();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/test";

        var result = await adapter.TryWriteProtocolErrorAsync(httpContext, CreateProjection());

        result.ShouldBeFalse();
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
    /// 验证：AbpEnvelopeProtocolAdapter 总是返回 false。
    /// 行为：不处理任何请求（默认适配器）。
    /// </summary>
    [Fact]
    public void Abp_Adapter_Should_Always_Return_False()
    {
        var adapter = new AbpEnvelopeProtocolAdapter();
        var httpContext = new DefaultHttpContext();

        var result = adapter.TryWriteProtocolErrorAsync(httpContext, CreateProjection()).Result;

        result.ShouldBeFalse();
    }

    /// <summary>
    /// 验证：SignalRProtocolAdapter 总是返回 false。
    /// 行为：不处理任何请求。
    /// </summary>
    [Fact]
    public void SignalR_Adapter_Should_Always_Return_False()
    {
        var adapter = new SignalRProtocolAdapter();
        var httpContext = new DefaultHttpContext();

        var result = adapter.TryWriteProtocolErrorAsync(httpContext, CreateProjection()).Result;

        result.ShouldBeFalse();
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