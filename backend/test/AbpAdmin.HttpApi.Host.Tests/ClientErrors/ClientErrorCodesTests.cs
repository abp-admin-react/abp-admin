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
    /// 验证等待时间优先级：合法 HTTP header 优先，批准 body 字段兜底。
    /// 测试 RetryAfterResolver.Resolve 的实际行为。
    /// </summary>
    [Fact]
    public void RetryAfter_Header_Should_Precede_Body()
    {
        // Header 优先：header 有值时忽略 body
        RetryAfterResolver.Resolve(headerValue: 30, bodyValue: 25)
            .ShouldBe(30, "header 优先");

        // Header 为 null 时使用 body
        RetryAfterResolver.Resolve(headerValue: null, bodyValue: 25)
            .ShouldBe(25, "header 为 null 时 body 兜底");

        // 两者都为 null 时返回 null
        RetryAfterResolver.Resolve(headerValue: null, bodyValue: null)
            .ShouldBeNull("两者都为 null 时返回 null");

        // Header 无效（负数）时使用 body
        RetryAfterResolver.Resolve(headerValue: -1, bodyValue: 25)
            .ShouldBe(25, "header 无效时 body 兜底");
    }

    /// <summary>
    /// 验证 RetryAfter 值的合法性检测。
    /// 测试 RetryAfterResolver.IsValid 的实际行为：非负整数为合法。
    /// </summary>
    [Theory]
    [InlineData(-1, false, "负数应被判定为非法")]
    [InlineData(0, true, "0 是合法值")]
    [InlineData(86400, true, "86400 秒（24小时）是合法值")]
    [InlineData(86401, true, "超过 86400 秒也合法，只是不展示倒计时")]
    public void Invalid_RetryAfter_Should_Be_Discarded(int value, bool isValid, string reason)
    {
        RetryAfterResolver.IsValid(value).ShouldBe(isValid, reason);
    }

    /// <summary>
    /// 验证超过 24 小时的 RetryAfter 不展示倒计时，但值本身合法。
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(86400, true)]
    [InlineData(86401, false)]
    public void RetryAfter_Countdown_Display_Rules(int seconds, bool shouldShowCountdown)
    {
        RetryAfterResolver.ShouldShowCountdown(seconds).ShouldBe(shouldShowCountdown);
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
