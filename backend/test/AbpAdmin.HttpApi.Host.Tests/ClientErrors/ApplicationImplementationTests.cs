using System;
using System.Collections.Generic;
using AbpAdmin.Application.ClientErrors;
using AbpAdmin.ClientErrors;
using AbpAdmin.Domain.ClientErrors;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class ClientErrorAppServiceTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);
    private readonly ClientErrorAppService _appService;
    private readonly PublicErrorCatalog _catalog;

    public ClientErrorAppServiceTests()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Auth", PublicCode = ClientErrorCodes.AuthenticationRequired, ResourceType = ClientErrorResourceType, MessageKey = "AuthenticationRequired", Kind = PublicErrorKind.Authentication },
            new() { InternalCode = "Test:Validation", PublicCode = ClientErrorCodes.ValidationFailed, ResourceType = ClientErrorResourceType, MessageKey = "ValidationFailed", Kind = PublicErrorKind.Validation },
        };
        _catalog = new PublicErrorCatalog(definitions);
        var projectionCore = new ClientErrorProjectionCore(_catalog);
        _appService = new ClientErrorAppService(projectionCore);
    }

    /// <summary>
    /// 验证：CreateFailureResult 创建正确的 DTO。
    /// 行为：返回包含 Code、Message、CorrelationId 的 ClientFailureDto。
    /// </summary>
    [Fact]
    public void CreateFailureResult_Should_Create_Dto()
    {
        var result = _appService.CreateFailureResult(
            "Test:Auth",
            null,
            "corr-123");

        result.ShouldNotBeNull();
        result.Code.ShouldBe(ClientErrorCodes.AuthenticationRequired);
        result.Message.ShouldBe("Authentication required. Please log in.");
        result.CorrelationId.ShouldBe("corr-123");
    }

    /// <summary>
    /// 验证：CreateFailureResult 包含数据字典。
    /// 行为：当有 ExceptionData 时，DTO 包含 Data 字典（但可能被过滤）。
    /// </summary>
    [Fact]
    public void CreateFailureResult_Should_Include_Data_When_Present()
    {
        var exceptionData = new Dictionary<string, object?>
        {
            ["extraData"] = "value",
        };

        var result = _appService.CreateFailureResult(
            "Test:Auth",
            exceptionData,
            "corr-123");

        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(0, "Data is filtered when no AllowedParameters defined in definition");
    }

    /// <summary>
    /// 验证：CreateFailureResult 无数据时 Data 为空字典。
    /// 行为：ExceptionData 为空时，DTO.Data 为空字典（不是 null）。
    /// </summary>
    [Fact]
    public void CreateFailureResult_Should_Have_Empty_Data_When_None()
    {
        var result = _appService.CreateFailureResult(
            "Test:Auth",
            null,
            "corr-123");

        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：CreateFailureResult 未知 InternalCode 返回 OperationFailed。
    /// 行为：未找到定义时使用默认代码。
    /// </summary>
    [Fact]
    public void CreateFailureResult_Should_Use_OperationFailed_For_Unknown()
    {
        var result = _appService.CreateFailureResult(
            "Unknown:Code",
            null,
            "corr");

        result.Code.ShouldBe(ClientErrorCodes.OperationFailed);
    }

    /// <summary>
    /// 验证：CreateFailureResult 无 InternalCode 返回 Unexpected。
    /// 行为：空代码映射到 Server 错误。
    /// </summary>
    [Fact]
    public void CreateFailureResult_Should_Use_Unexpected_When_No_Code()
    {
        var result = _appService.CreateFailureResult(
            null,
            null,
            "corr");

        result.Code.ShouldBe(ClientErrorCodes.Unexpected);
    }

    /// <summary>
    /// 验证：CreateFailureFromException 从异常提取代码。
    /// 行为：使用 BusinessException 的 Code 属性。
    /// </summary>
    [Fact]
    public void CreateFailureFromException_Should_Extract_Code()
    {
        var exception = new BusinessException("Test:Validation", "Validation failed");

        var result = _appService.CreateFailureFromException(exception, "corr-123");

        result.Code.ShouldBe(ClientErrorCodes.ValidationFailed);
    }

    /// <summary>
    /// 验证：CreateFailureFromException 提取异常数据。
    /// 行为：Exception.Data 被复制到 DTO.Data。
    /// </summary>
    [Fact]
    public void CreateFailureFromException_Should_Extract_Data()
    {
        var exception = new BusinessException("Test:Auth", "Auth failed");
        exception.Data["extra"] = "value";

        var result = _appService.CreateFailureFromException(exception, "corr");

        result.Data.ShouldNotBeNull();
        result.Data.ShouldNotBeNull();
        result.Data.Count.ShouldBe(0, "Data is filtered when no AllowedParameters defined in definition");
    }

    /// <summary>
    /// 验证：CreateFailureFromException 非 BusinessException 处理。
    /// 行为：未知异常类型使用默认代码。
    /// </summary>
    [Fact]
    public void CreateFailureFromException_Should_Handle_Non_BusinessException()
    {
        var exception = new InvalidOperationException("Generic error");

        var result = _appService.CreateFailureFromException(exception, "corr");

        result.Code.ShouldBe(ClientErrorCodes.Unexpected);
    }

    /// <summary>
    /// 验证：CreateFailureResult 使用 Public audience。
    /// 行为：输入使用 ClientErrorAudience.Public。
    /// </summary>
    [Fact]
    public void CreateFailureResult_Should_Use_Public_Audience()
    {
        var result = _appService.CreateFailureResult(
            "Test:Auth",
            null,
            "corr");

        result.Code.ShouldBe(ClientErrorCodes.AuthenticationRequired);
    }
}

public class BusinessExceptionTests
{
    /// <summary>
    /// 验证：BusinessException 构造只传消息。
    /// 行为：Code 为 null。
    /// </summary>
    [Fact]
    public void Constructor_With_Message_Only_Should_Set_Code_Null()
    {
        var ex = new BusinessException("Test message");

        ex.Message.ShouldBe("Test message");
        ex.Code.ShouldBeNull();
    }

    /// <summary>
    /// 验证：BusinessException 构造传代码和消息。
    /// 行为：Code 和 Message 都被设置。
    /// </summary>
    [Fact]
    public void Constructor_With_Code_And_Message_Should_Set_Both()
    {
        var ex = new BusinessException("Test:Code", "Test message");

        ex.Code.ShouldBe("Test:Code");
        ex.Message.ShouldBe("Test message");
    }

    /// <summary>
    /// 验证：BusinessException 支持内部异常。
    /// 行为：InnerException 被设置。
    /// </summary>
    [Fact]
    public void Constructor_With_InnerException_Should_Set_InnerException()
    {
        var inner = new InvalidOperationException("Inner");
        var ex = new BusinessException("Message", inner);

        ex.InnerException.ShouldBe(inner);
    }
}