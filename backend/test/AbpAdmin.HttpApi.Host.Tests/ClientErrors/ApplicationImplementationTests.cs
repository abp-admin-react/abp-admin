using System;
using System.Collections.Generic;
using AbpAdmin.ClientErrors;
using AbpAdmin.Domain.ClientErrors;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class ClientFailureMapperTests
{
    private static readonly Type ClientErrorResourceType = typeof(ClientErrorResource);
    private readonly ClientFailureMapper _mapper;
    private readonly PublicErrorCatalog _catalog;

    public ClientFailureMapperTests()
    {
        var definitions = new List<PublicErrorDefinition>
        {
            new() { InternalCode = "Test:Auth", PublicCode = ClientErrorCodes.AuthenticationRequired, ResourceType = ClientErrorResourceType, MessageKey = "AuthenticationRequired", Kind = PublicErrorKind.Authentication },
            new() { InternalCode = "Test:Validation", PublicCode = ClientErrorCodes.ValidationFailed, ResourceType = ClientErrorResourceType, MessageKey = "ValidationFailed", Kind = PublicErrorKind.Validation },
        };
        _catalog = PublicErrorCatalog.FromDefinitions(definitions);
        var projectionCore = new ClientErrorProjectionCore(_catalog);
        _mapper = new ClientFailureMapper(projectionCore);
    }

    [Fact]
    public void CreateFailureResult_Should_Create_Dto()
    {
        var result = _mapper.CreateFailureResult(
            "Test:Auth",
            null,
            "corr-123");

        result.ShouldNotBeNull();
        result.Code.ShouldBe(ClientErrorCodes.AuthenticationRequired);
        result.Message.ShouldBe("Authentication required. Please log in.");
        result.CorrelationId.ShouldBe("corr-123");
    }

    [Fact]
    public void CreateFailureResult_Should_Include_Data_When_Present()
    {
        var exceptionData = new Dictionary<string, object?>
        {
            ["extraData"] = "value",
        };

        var result = _mapper.CreateFailureResult(
            "Test:Auth",
            exceptionData,
            "corr-123");

        result.Data.ShouldNotBeNull();
        result.Data.ContainsKey("extraData").ShouldBeFalse();
    }

    [Fact]
    public void CreateFailureResult_Should_Have_Empty_Data_When_None()
    {
        var result = _mapper.CreateFailureResult(
            "Test:Auth",
            null,
            null);

        result.Data.ShouldNotBeNull();
        result.Data.ContainsKey(ClientErrorFieldNames.CorrelationId).ShouldBeFalse();
    }

    [Fact]
    public void CreateFailureResult_Should_Use_OperationFailed_For_Unknown()
    {
        var result = _mapper.CreateFailureResult(
            "Unknown:Code",
            null,
            "corr");

        result.Code.ShouldBe(ClientErrorCodes.OperationFailed);
    }

    [Fact]
    public void CreateFailureResult_Should_Use_Unexpected_When_No_Code()
    {
        var result = _mapper.CreateFailureResult(
            null,
            null,
            "corr");

        result.Code.ShouldBe(ClientErrorCodes.Unexpected);
    }

    [Fact]
    public void CreateFailureFromException_Should_Extract_Code()
    {
        var exception = new BusinessException("Test:Validation", "Validation failed");

        var result = _mapper.CreateFailureFromException(exception, "corr-123");

        result.Code.ShouldBe(ClientErrorCodes.ValidationFailed);
    }

    [Fact]
    public void CreateFailureFromException_Should_Extract_Data()
    {
        var exception = new BusinessException("Test:Auth", "Auth failed");
        exception.Data["extra"] = "value";

        var result = _mapper.CreateFailureFromException(exception, "corr");

        result.Data.ShouldNotBeNull();
        result.Data.ContainsKey("extra").ShouldBeFalse();
    }

    [Fact]
    public void CreateFailureFromException_Should_Handle_Non_BusinessException()
    {
        var exception = new InvalidOperationException("Generic error");

        var result = _mapper.CreateFailureFromException(exception, "corr");

        result.Code.ShouldBe(ClientErrorCodes.Unexpected);
    }

    [Fact]
    public void CreateFailureResult_Should_Put_CorrelationId_In_Data()
    {
        var result = _mapper.CreateFailureResult(
            "Test:Auth",
            null,
            "corr");

        result.Data[ClientErrorFieldNames.CorrelationId].ShouldBe("corr");
    }
}
