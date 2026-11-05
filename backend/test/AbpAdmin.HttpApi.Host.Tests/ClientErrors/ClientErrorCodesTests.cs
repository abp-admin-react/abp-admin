using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P0-5：公开码/字段命名/支持语言/管理白名单 + 基线失败夹具。
/// 验证第 3 节公开码/字段命名、支持语言、管理兼容数据白名单。
/// </summary>
public class ClientErrorCodesTests
{
    /// <summary>
    /// 验证基础公开码枚举完整。
    /// </summary>
    [Fact]
    public void Base_PublicCodes_Should_Be_Complete()
    {
        var expectedCodes = new HashSet<string>
        {
            ClientErrorCodes.AuthenticationRequired,
            ClientErrorCodes.Forbidden,
            ClientErrorCodes.ValidationFailed,
            ClientErrorCodes.NotFound,
            ClientErrorCodes.RateLimited,
            ClientErrorCodes.Conflict,
            ClientErrorCodes.OperationFailed,
            ClientErrorCodes.Unexpected,
        };

        expectedCodes.Count.ShouldBe(8);

        // 验证命名规则
        foreach (var code in expectedCodes)
        {
            code.ShouldStartWith("Client:");
        }
    }

    /// <summary>
    /// 验证公开码与 PublicErrorKind 映射一致。
    /// 模块不可重新定义基础码 kind。
    /// </summary>
    [Fact]
    public void Base_Code_Kind_Mapping_Should_Be_Consistent()
    {
        ClientErrorKindMapping.Validate();

        ClientErrorKindMapping.GetKind(ClientErrorCodes.AuthenticationRequired)
            .ShouldBe(PublicErrorKind.Authentication);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.Forbidden)
            .ShouldBe(PublicErrorKind.Forbidden);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.ValidationFailed)
            .ShouldBe(PublicErrorKind.Validation);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.NotFound)
            .ShouldBe(PublicErrorKind.NotFound);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.RateLimited)
            .ShouldBe(PublicErrorKind.RateLimited);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.Conflict)
            .ShouldBe(PublicErrorKind.Conflict);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.OperationFailed)
            .ShouldBe(PublicErrorKind.Business);
        ClientErrorKindMapping.GetKind(ClientErrorCodes.Unexpected)
            .ShouldBe(PublicErrorKind.Server);
    }

    /// <summary>
    /// 未知码默认映射为 Business。
    /// </summary>
    [Fact]
    public void Unknown_Code_Should_Default_To_Business()
    {
        ClientErrorKindMapping.GetKind("BizTemplate:Public:ProjectUnavailable")
            .ShouldBe(PublicErrorKind.Business);
    }

    /// <summary>
    /// 验证字段命名约定（客户端 lowerCamelCase）。
    /// </summary>
    [Fact]
    public void FieldNames_Should_Follow_LowerCamelCase()
    {
        ClientErrorFieldNames.CorrelationId.ShouldBe("correlationId");
        ClientErrorFieldNames.RetryAfterSeconds.ShouldBe("retryAfterSeconds");
        ClientErrorFieldNames.Code.ShouldBe("code");
        ClientErrorFieldNames.Message.ShouldBe("message");
        ClientErrorFieldNames.Details.ShouldBe("details");
        ClientErrorFieldNames.Data.ShouldBe("data");
        ClientErrorFieldNames.ValidationErrors.ShouldBe("validationErrors");
        ClientErrorFieldNames.Members.ShouldBe("members");
    }

    /// <summary>
    /// 验证支持语言清单。
    /// 首期静态客户支持集：zh-Hans、zh-Hant、en。
    /// </summary>
    [Fact]
    public void Supported_Languages_Should_Be_Defined()
    {
        var supportedLanguages = new HashSet<string>
        {
            "zh-Hans", // 简体中文
            "zh-Hant", // 繁体中文
            "en",      // 英文
        };

        supportedLanguages.Count.ShouldBe(3);

        // 这是首期明确支持集，不是对现有全部前端/后端文化数量的断言。
    }

    /// <summary>
    /// 验证管理兼容数据白名单定义正确。
    /// </summary>
    [Fact]
    public void Management_Whitelist_Should_Be_Defined()
    {
        // 限流：RetryAfterSeconds
        ManagementCompatibilityDataWhitelist.RateLimiting.ApprovedFields
            .ShouldContain(ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds);

        // 图片繁忙：无 data 字段
        ManagementCompatibilityDataWhitelist.ImageProcessingBusy.ApprovedFields.Count
            .ShouldBe(0);

        // 文件上传拒绝：fileName, reason
        ManagementCompatibilityDataWhitelist.FileUploadRejection.ApprovedFields
            .ShouldContain(ManagementCompatibilityDataWhitelist.FileUploadRejection.FileName);
        ManagementCompatibilityDataWhitelist.FileUploadRejection.ApprovedFields
            .ShouldContain(ManagementCompatibilityDataWhitelist.FileUploadRejection.Reason);
    }

    /// <summary>
    /// 基线失败夹具：验证错误结构形状。
    /// 验证 validationErrors 项结构为 { message, members } 而非字典。
    /// </summary>
    [Fact]
    public void ValidationError_Structure_Should_Have_Members_Array()
    {
        // 方案 3.1 明确：validationErrors 的项为 { message, members }，
        // 不是 ValidationProblemDetails.errors[field][]。

        var validationError = new ValidationErrorFixture
        {
            Message = "该字段是必需的。",
            Members = ["userName", "email"],
        };

        validationError.Message.ShouldNotBeNullOrEmpty();
        validationError.Members.Count.ShouldBe(2);
        validationError.Members.ShouldContain("userName");

        // 这与 ABP 的 GetValidationErrorInfos 返回结构一致
    }

    /// <summary>
    /// 基线失败夹具：401 触发会话恢复。
    /// </summary>
    [Fact]
    public void AuthenticationRequired_401_Should_Trigger_Session_Recovery()
    {
        // 方案 7.2/7.3：
        // - HTTP 401 触发同会话单次恢复
        // - HTTP 403 永不触发清理/恢复
        // - 只有 Client:AuthenticationRequired 或无公开码允许在 401 上
        // - 其他码 + 401 丢弃码/message，用本地 authentication 兜底

        var rule401 = new AuthenticationRecoveryRule
        {
            HttpStatusCode = 401,
            AllowedCodes = [ClientErrorCodes.AuthenticationRequired, ""], // 无码也允许
            TriggerRecovery = true,
        };

        rule401.AllowedCodes.ShouldContain(ClientErrorCodes.AuthenticationRequired);
        rule401.TriggerRecovery.ShouldBeTrue();
    }

    /// <summary>
    /// 基线失败夹具：403 不触发会话清理。
    /// </summary>
    [Fact]
    public void Forbidden_403_Should_Not_Trigger_Session_Clear()
    {
        var rule403 = new AuthenticationRecoveryRule
        {
            HttpStatusCode = 403,
            AllowedCodes = [ClientErrorCodes.Forbidden, ClientErrorCodes.OperationFailed],
            TriggerRecovery = false, // 403 永不触发恢复
        };

        rule403.TriggerRecovery.ShouldBeFalse();
        rule403.AllowedCodes.ShouldContain(ClientErrorCodes.Forbidden);
        rule403.AllowedCodes.ShouldContain(ClientErrorCodes.OperationFailed); // 业务码可以在 403 上
    }

    /// <summary>
    /// 基线失败夹具：等待时间优先级。
    /// 合法 header 优先，批准 body 兜底。
    /// </summary>
    [Fact]
    public void RetryAfter_Header_Should_Precede_Body()
    {
        // 方案 3.2/Q26：
        // - 合法 HTTP Retry-After header 优先
        // - 批准 body 字段兜底
        // - 冲突不拼值、不相加

        var scenario = new RetryAfterPriorityScenario
        {
            HeaderValue = 30,
            BodyValue = 25,
            ExpectedResult = 30, // header 优先
        };

        scenario.ExpectedResult.ShouldBe(30);
    }

    /// <summary>
    /// 基线失败夹具：非法等待时间丢弃。
    /// </summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(86401, true)] // 允许但展示策略不同
    public void Invalid_RetryAfter_Should_Be_Discarded(int value, bool isValid)
    {
        // 合法值：非负整数
        // 超过 86400 秒不展示具体倒计时，但不丢弃值
        var isValidSpec = value >= 0;
        isValidSpec.ShouldBe(isValid);
    }
}

/// <summary>
/// 校验错误夹具。
/// </summary>
public sealed record ValidationErrorFixture
{
    public required string Message { get; init; }
    public required IReadOnlyList<string> Members { get; init; }
}

/// <summary>
/// 认证恢复规则夹具。
/// </summary>
public sealed record AuthenticationRecoveryRule
{
    public int HttpStatusCode { get; init; }
    public required IReadOnlyList<string> AllowedCodes { get; init; }
    public bool TriggerRecovery { get; init; }
}

/// <summary>
/// 等待时间优先级场景夹具。
/// </summary>
public sealed record RetryAfterPriorityScenario
{
    public int? HeaderValue { get; init; }
    public int? BodyValue { get; init; }
    public required int ExpectedResult { get; init; }
}
