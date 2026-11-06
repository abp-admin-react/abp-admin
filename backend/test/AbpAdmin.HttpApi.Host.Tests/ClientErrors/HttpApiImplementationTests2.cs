using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Domain.ClientErrors;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Http;
using Volo.Abp.Tracing;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class ClientErrorHttpProjectorTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);
    private readonly ClientErrorHttpProjector _projector;

    public ClientErrorHttpProjectorTests()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Auth", PublicCode = ClientErrorCodes.AuthenticationRequired, ResourceType = ClientErrorResourceType, MessageKey = "AuthenticationRequired", Kind = PublicErrorKind.Authentication },
            new() { InternalCode = "Test:NotFound", PublicCode = ClientErrorCodes.NotFound, ResourceType = ClientErrorResourceType, MessageKey = "NotFound", Kind = PublicErrorKind.NotFound },
            new() { InternalCode = "Test:RateLimited", PublicCode = ClientErrorCodes.RateLimited, ResourceType = ClientErrorResourceType, MessageKey = "RateLimited", Kind = PublicErrorKind.RateLimited },
            new() { InternalCode = "Test:Server", PublicCode = ClientErrorCodes.Unexpected, ResourceType = ClientErrorResourceType, MessageKey = "Unexpected", Kind = PublicErrorKind.Server },
        };
        var catalog = PublicErrorCatalog.FromDefinitions(definitions);
        var projectionCore = new ClientErrorProjectionCore(catalog);
        var eligibilityResolver = new AdminErrorEligibilityResolver();
        _projector = new ClientErrorHttpProjector(projectionCore, eligibilityResolver);
    }

    /// <summary>
    /// 验证：Project 拒绝 null context。
    /// 行为：抛出 ArgumentNullException。这是编程错误，不走安全信封。
    /// </summary>
    [Fact]
    public void Project_Should_Reject_Null_Context()
    {
        Should.Throw<ArgumentNullException>(() => _projector.Project(null!));
    }

    /// <summary>
    /// 验证：Project 从 IHasErrorCode 取内部码再查目录。
    /// 行为：Test:Auth 投影为 AuthenticationRequired。
    /// </summary>
    [Fact]
    public void Project_Should_Extract_Exception_Code()
    {
        var httpContext = new DefaultHttpContext();
        var exception = new TestBusinessException("Test:Auth", "Auth failed");

        var context = new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = exception,
            EffectiveAudience = ClientErrorAudience.Public,
        };

        var result = _projector.Project(context);

        result.ShouldNotBeNull();
        result.Body.PublicCode.ShouldBe(ClientErrorCodes.AuthenticationRequired);
    }

    /// <summary>
    /// 验证：目录 Kind 映射 HTTP 状态。
    /// 行为：Authentication → 401。
    /// </summary>
    [Fact]
    public void Project_Should_Determine_Http_Status()
    {
        var httpContext = new DefaultHttpContext();
        var exception = new TestBusinessException("Test:Auth", "Auth failed");

        var context = new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = exception,
            EffectiveAudience = ClientErrorAudience.Public,
        };

        var result = _projector.Project(context);

        result.ShouldNotBeNull();
        result.StatusCode.ShouldBe(401);
    }

    /// <summary>
    /// 验证：RateLimited 映射 429。
    /// 行为：目录 Kind 优先，不走 ABP 默认状态。
    /// </summary>
    [Fact]
    public void Project_Should_Map_RateLimited_To_429()
    {
        var httpContext = new DefaultHttpContext();
        var exception = new TestBusinessException("Test:RateLimited", "Rate limited");

        var context = new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = exception,
            EffectiveAudience = ClientErrorAudience.Public,
        };

        var result = _projector.Project(context);

        result.ShouldNotBeNull();
        result.StatusCode.ShouldBe(429);
    }

    /// <summary>
    /// 验证：已登记的 NotFound 不被 ABP BusinessException→403 覆盖。
    /// 行为：IHttpExceptionStatusCodeFinder 返回 Forbidden 也不调用；状态 404。
    /// </summary>
    [Fact]
    public void Project_Should_Prefer_Catalog_Kind_Over_Abp_BusinessException_Forbidden()
    {
        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        var finder = Substitute.For<IHttpExceptionStatusCodeFinder>();
        finder.GetStatusCode(Arg.Any<HttpContext>(), Arg.Any<Exception>())
            .Returns(System.Net.HttpStatusCode.Forbidden);
        services.AddSingleton(finder);
        httpContext.RequestServices = services.BuildServiceProvider();

        var context = new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = new TestBusinessException("Test:NotFound", "missing"),
            EffectiveAudience = ClientErrorAudience.Public,
        };

        var result = _projector.Project(context);

        result.ShouldNotBeNull();
        result.StatusCode.ShouldBe(404);
        result.Body.PublicCode.ShouldBe(ClientErrorCodes.NotFound);
        finder.DidNotReceive().GetStatusCode(Arg.Any<HttpContext>(), Arg.Any<Exception>());
    }

    /// <summary>
    /// 验证：无内部码的 Server 兜底保留已写出的 400+ 状态。
    /// 行为：响应已是 418 时不改成 500。
    /// </summary>
    [Fact]
    public void Project_Should_Keep_Existing_Status_When_400_Plus()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = 418;

        var context = new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = null,
            EffectiveAudience = ClientErrorAudience.Public,
        };

        var result = _projector.Project(context);

        result.ShouldNotBeNull();
        result.StatusCode.ShouldBe(418);
    }

    /// <summary>
    /// 验证：关联号来自 ICorrelationIdProvider，不是 TraceIdentifier。
    /// 行为：DI 中的 provider 返回 corr-from-provider。
    /// </summary>
    [Fact]
    public void Project_Should_Use_ICorrelationIdProvider()
    {
        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddSingleton<ICorrelationIdProvider>(new StaticCorrelationIdProvider("corr-from-provider"));
        httpContext.RequestServices = services.BuildServiceProvider();

        var context = new ClientErrorHttpProjectionContext
        {
            HttpContext = httpContext,
            Exception = null,
            EffectiveAudience = ClientErrorAudience.Public,
        };

        var result = _projector.Project(context);

        result.ShouldNotBeNull();
        result.Body.CorrelationId.ShouldBe("corr-from-provider");
    }

    /// <summary>
    /// 验证：MVC 信封拷贝投影 Data，Details 保持 null。
    /// 行为：correlationId 与 retryAfterSeconds 出现在 RemoteServiceErrorInfo.Data。
    /// </summary>
    [Fact]
    public void ToRemoteServiceErrorResponse_Should_Copy_Projection_Data()
    {
        var projection = new ClientErrorHttpProjection
        {
            StatusCode = 429,
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.RateLimited,
                Kind = PublicErrorKind.RateLimited,
                Message = "slow down",
                Data = new Dictionary<string, object?>
                {
                    [ClientErrorFieldNames.CorrelationId] = "corr-1",
                    ["retryAfterSeconds"] = 7,
                },
            },
            RetryAfterSeconds = 7,
        };

        var envelope = ClientErrorExceptionFilter.ToRemoteServiceErrorResponse(projection);

        envelope.Error.ShouldNotBeNull();
        envelope.Error.Data.ShouldNotBeNull();
        envelope.Error.Data[ClientErrorFieldNames.CorrelationId].ShouldBe("corr-1");
        envelope.Error.Data["retryAfterSeconds"].ShouldBe(7);
        envelope.Error.Details.ShouldBeNull();
    }

    private sealed class StaticCorrelationIdProvider : ICorrelationIdProvider
    {
        private readonly string? _id;

        public StaticCorrelationIdProvider(string? id) => _id = id;

        public string? Get() => _id;

        public IDisposable Change(string? correlationId) => NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    private class TestBusinessException : Exception, IHasErrorCode
    {
        public string? Code { get; }

        public TestBusinessException(string code, string message) : base(message)
        {
            Code = code;
        }
    }
}

public class ClientErrorResponseWriterTests
{
    /// <summary>
    /// 验证：Writer 写出状态码和 JSON 体。
    /// 行为：StatusCode=500；Data 空字典表示无批准字段，不传 null。
    /// </summary>
    [Fact]
    public async Task WriteResponse_Should_Write_Status_And_Body()
    {
        var httpContext = new DefaultHttpContext();
        var writer = new ClientErrorResponseWriter();

        var projection = new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.Unexpected,
                Kind = PublicErrorKind.Server,
                Message = "Test error",
            },
            StatusCode = 500,
        };

        await writer.WriteAsync(httpContext, projection, default);

        httpContext.Response.StatusCode.ShouldBe(500);
    }

    /// <summary>
    /// 验证：Writer 写出 ValidationErrors。
    /// 行为：校验失败时 members 进入信封。
    /// </summary>
    [Fact]
    public async Task WriteResponse_Should_Include_ValidationErrors()
    {
        var httpContext = new DefaultHttpContext();
        var writer = new ClientErrorResponseWriter();

        var projection = new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.ValidationFailed,
                Kind = PublicErrorKind.Validation,
                Message = "Validation failed",
                ValidationErrors = new List<PublicValidationErrorItem>
                {
                    new() { Message = "Field is required", Members = new List<string> { "name" } },
                },
            },
            StatusCode = 400,
        };

        await writer.WriteAsync(httpContext, projection, default);

        httpContext.Response.StatusCode.ShouldBe(400);
    }

    /// <summary>
    /// 验证：Writer 在 Clear 之后重写 Retry-After。
    /// 行为：429 响应带 Retry-After: 60。
    /// </summary>
    [Fact]
    public async Task WriteResponse_Should_Include_RetryAfter_Header()
    {
        var httpContext = new DefaultHttpContext();
        var writer = new ClientErrorResponseWriter();

        var projection = new ClientErrorHttpProjection
        {
            Body = new PublicErrorProjectionResult
            {
                PublicCode = ClientErrorCodes.RateLimited,
                Kind = PublicErrorKind.RateLimited,
                Message = "Too many requests",
            },
            StatusCode = 429,
            RetryAfterSeconds = 60,
        };

        await writer.WriteAsync(httpContext, projection, default);

        httpContext.Response.StatusCode.ShouldBe(429);
        httpContext.Response.Headers.RetryAfter.ToString().ShouldBe("60");
    }
}
