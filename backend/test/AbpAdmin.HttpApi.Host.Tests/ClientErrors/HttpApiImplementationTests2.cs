using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Domain.ClientErrors;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Tracing;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class ClientErrorHttpProjectorTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);
    private readonly ClientErrorHttpProjector _projector;
    private readonly PublicErrorCatalog _catalog;

    public ClientErrorHttpProjectorTests()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Auth", PublicCode = ClientErrorCodes.AuthenticationRequired, ResourceType = ClientErrorResourceType, MessageKey = "AuthenticationRequired", Kind = PublicErrorKind.Authentication },
            new() { InternalCode = "Test:RateLimited", PublicCode = ClientErrorCodes.RateLimited, ResourceType = ClientErrorResourceType, MessageKey = "RateLimited", Kind = PublicErrorKind.RateLimited },
            new() { InternalCode = "Test:Server", PublicCode = ClientErrorCodes.Unexpected, ResourceType = ClientErrorResourceType, MessageKey = "Unexpected", Kind = PublicErrorKind.Server },
        };
        _catalog = PublicErrorCatalog.FromDefinitions(definitions);
        var projectionCore = new ClientErrorProjectionCore(_catalog);
        var eligibilityResolver = new AdminErrorEligibilityResolver();
        _projector = new ClientErrorHttpProjector(projectionCore, eligibilityResolver);
    }

    /// <summary>
    /// 验证：Project 拒绝 null context�?    /// 行为：抛�?ArgumentNullException�?    /// </summary>
    [Fact]
    public void Project_Should_Reject_Null_Context()
    {
        Should.Throw<ArgumentNullException>(() => _projector.Project(null!));
    }

    /// <summary>
    /// 验证：Project 提取异常代码�?    /// 行为：从异常中提�?Code 属性�?    /// </summary>
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
    /// 验证：Project 确定 HTTP 状态码�?    /// 行为：根�?kind 映射到正确的状态码�?    /// </summary>
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

        result.StatusCode.ShouldBe(401);
    }

    /// <summary>
    /// 验证：Project 映射 RateLimited �?429�?    /// 行为：Kind.RateLimited �?429�?    /// </summary>
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

        result.StatusCode.ShouldBe(429);
    }

    /// <summary>
    /// 验证：Project 保留已有状态码�?    /// 行为：如果已�?400+ 状态码，保留不变�?    /// </summary>
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

        result.StatusCode.ShouldBe(418);
    }

    /// <summary>
    /// 验证：Project 使用 TraceIdentifier 作为 CorrelationId�?    /// 行为：从 HttpContext 获取�?    /// </summary>
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

        result!.Body.CorrelationId.ShouldBe("corr-from-provider");
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
    /// 验证：ResponseWriter 写入状态码�?JSON 体�?    /// 行为：响应包含正确的状态码和序列化内容�?    /// </summary>
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
                Data = null,
            },
            StatusCode = 500,
        };

        await writer.WriteAsync(httpContext, projection, default);

        httpContext.Response.StatusCode.ShouldBe(500);
    }

    /// <summary>
    /// 验证：ResponseWriter 写入 ValidationErrors�?    /// 行为：当有验证错误时，包含在响应中�?    /// </summary>
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
    /// 验证：ResponseWriter 处理 RetryAfter�?    /// 行为：包�?Retry-After 头�?    /// </summary>
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
    }
}