using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P1 契约测试：ClientFailureDto 形状与序列化（方案 3.3）。
/// 验证字段名、默认值、JSON 序列化 camelCase。
/// </summary>
public class ClientFailureDtoContractTests
{
    /// <summary>
    /// 验证：DTO 属性名称符合约定（反射检查，确保前后端一致）。
    /// </summary>
    [Fact]
    public void PropertyNames_Should_Match_Convention()
    {
        var properties = typeof(ClientFailureDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet();

        properties.ShouldContain("Code");
        properties.ShouldContain("Message");
        properties.ShouldContain("CorrelationId");
        properties.ShouldContain("Data");
        properties.Count.ShouldBe(4, "应恰好有 4 个属性");
    }

    /// <summary>
    /// 验证：默认值正确（Code 默认 OperationFailed）。
    /// </summary>
    [Fact]
    public void Defaults_Should_Be_Correct()
    {
        var dto = new ClientFailureDto();

        dto.Code.ShouldBe(ClientErrorCodes.OperationFailed);
        dto.Message.ShouldBeEmpty();
        dto.CorrelationId.ShouldBeNull();
        dto.Data.ShouldNotBeNull();
        dto.Data.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：JSON 序列化使用 camelCase（ABP 默认行为）。
    /// </summary>
    [Fact]
    public void Json_Serialization_Should_Use_CamelCase()
    {
        var dto = new ClientFailureDto
        {
            Code = "Test:Failure",
            Message = "Test message",
            CorrelationId = "abc123",
            Data = new Dictionary<string, object?>
            {
                ["retryAfterSeconds"] = 30,
            },
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

        json.ShouldContain("\"code\":\"Test:Failure\"");
        json.ShouldContain("\"message\":\"Test message\"");
        json.ShouldContain("\"correlationId\":\"abc123\"");
        json.ShouldContain("\"retryAfterSeconds\":30");
    }

    /// <summary>
    /// 验证：CorrelationId 为 null 时不序列化（可空处理）。
    /// </summary>
    [Fact]
    public void Null_CorrelationId_Should_Be_Serialized_As_Null()
    {
        var dto = new ClientFailureDto
        {
            Code = "Test:Code",
            Message = "Message",
            CorrelationId = null,
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        json.ShouldContain("\"correlationId\":null");
    }

    /// <summary>
    /// 验证：Data 字典为空时序列化为空对象。
    /// </summary>
    [Fact]
    public void Empty_Data_Should_Be_Serialized_As_Empty_Object()
    {
        var dto = new ClientFailureDto
        {
            Code = "Test:Code",
            Message = "Message",
        };

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        json.ShouldContain("\"data\":{}");
    }
}
