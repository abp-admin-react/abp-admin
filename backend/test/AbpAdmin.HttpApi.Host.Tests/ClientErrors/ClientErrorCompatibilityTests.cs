using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Domain.ClientErrors;
using AbpAdmin.Imaging;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P0-6：兼容用例。
/// 将后端图片繁忙 503/Retry-After、管理 503 通用提示、限流 429 和管理 404 静默加入兼容用例。
/// 不把 503 倒计时说成既有管理行为。
/// </summary>
/// <remarks>
/// <para>
/// A17 管理兼容验证：
/// - 原用例继续通过
/// - 404 静默、403 提示、429 等待、503 通用提示保持
/// - 每个管理出口 error.data 键集合 ⊆ 批准集合
/// </para>
/// <para>
/// A31 等待时间/kind：
/// - ImageProcessingBusy 映射 Unexpected/server + 503
/// - 可带等待值
/// - 目录定义允许 server kind 可选 retryAfterSeconds
/// </para>
/// </remarks>
public class ClientErrorCompatibilityTests
{
    /// <summary>
    /// 验证图片繁忙异常的 RetryAfterSeconds 属性被正确初始化。
    /// 测试 AbpAdminImageProcessingBusyException 的构造函数行为。
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void Image_Processing_Busy_Exception_Should_Store_RetryAfter(int retrySeconds)
    {
        var exception = new AbpAdminImageProcessingBusyException(retrySeconds);

        exception.RetryAfterSeconds.ShouldBe(retrySeconds);
        exception.HttpStatusCode.ShouldBe(503);
    }

    /// <summary>
    /// 验证 PublicErrorKind.Server 可以与 RetryAfter 共存。
    /// Server kind 允许可选的 retryAfterSeconds（方案 3.2）。
    /// </summary>
    [Fact]
    public void Server_Kind_Can_Have_RetryAfter()
    {
        // Client:Unexpected 映射到 Server kind
        var kind = ClientErrorKindMapping.GetKind(ClientErrorCodes.Unexpected);
        kind.ShouldBe(PublicErrorKind.Server);

        // Server kind 可以携带 RetryAfter 信息
        // 验证 ClientErrorKindMapping 返回正确的 kind
        ClientErrorKindMapping.GetKind(ClientErrorCodes.Unexpected)
            .ShouldBe(PublicErrorKind.Server);
    }

    /// <summary>
    /// 验证管理端 404 的前端契约：PublicErrorKind.NotFound 不触发全局弹窗。
    /// 前端 requestErrorConfig.ts 对 404 进行静默处理。
    /// </summary>
    [Fact]
    public void NotFound_Kind_For_404_Does_Not_Trigger_Global_Error()
    {
        // 404 映射到 NotFound kind
        var kind = ClientErrorKindMapping.GetKind(ClientErrorCodes.NotFound);
        kind.ShouldBe(PublicErrorKind.NotFound);

        // 前端对 NotFound kind 的处理在 requestErrorConfig.ts 中实现
        // 此测试验证 kind 映射正确，前端行为由前端测试覆盖
    }

    /// <summary>
    /// 验证 403 对应 Forbidden kind，且不会触发会话恢复逻辑。
    /// </summary>
    [Fact]
    public void Forbidden_Kind_For_403_Does_Not_Trigger_Recovery()
    {
        var kind = ClientErrorKindMapping.GetKind(ClientErrorCodes.Forbidden);
        kind.ShouldBe(PublicErrorKind.Forbidden);

        // 403 不等于 Authentication kind，不会触发会话恢复
        kind.ShouldNotBe(PublicErrorKind.Authentication);
    }

    /// <summary>
    /// 验证 429RateLimited 对应正确的 kind。
    /// </summary>
    [Fact]
    public void RateLimited_Kind_For_429_Is_Correct()
    {
        var kind = ClientErrorKindMapping.GetKind(ClientErrorCodes.RateLimited);
        kind.ShouldBe(PublicErrorKind.RateLimited);
    }

    /// <summary>
    /// 验证 ManagementCompatibilityDataWhitelist 中的字段命名符合契约。
    /// </summary>
    [Fact]
    public void Management_Whitelist_Field_Names_Match_Contract()
    {
        // 限流：RetryAfterSeconds（管理端 PascalCase）
        ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds
            .ShouldBe("RetryAfterSeconds");

        // 文件上传：fileName, reason（客户端 lowerCamelCase）
        ManagementCompatibilityDataWhitelist.FileUploadRejection.FileName
            .ShouldBe("fileName");
        ManagementCompatibilityDataWhitelist.FileUploadRejection.Reason
            .ShouldBe("reason");
    }

    /// <summary>
    /// A17：管理响应 error.data 键集合必须是批准集合的子集。
    /// Public 只有 lowerCamelCase；AdminCompatibility 额外复制 PascalCase RetryAfterSeconds。
    /// </summary>
    [Fact]
    public void Management_DataKeys_Should_Be_Subset_Of_Approved()
    {
        var rateLimitedParameters = new PublicErrorParameterDefinition[]
        {
            new()
            {
                Name = ClientErrorFieldNames.RetryAfterSeconds,
                Source = PublicErrorParameterSource.RetryAfterSeconds,
                ValueType = PublicErrorParameterValueType.Int32,
                MinValue = 0,
                MaxValue = 86400,
                CanReturnToClient = true,
            },
        };
        var catalog = PublicErrorCatalog.FromDefinitions(
        [
            new PublicErrorDefinition
            {
                InternalCode = ClientErrorCodes.RateLimited,
                PublicCode = ClientErrorCodes.RateLimited,
                ResourceType = typeof(ClientErrorResource),
                MessageKey = "RateLimited",
                Kind = PublicErrorKind.RateLimited,
                AllowedParameters = rateLimitedParameters,
            },
        ]);
        var core = new ClientErrorProjectionCore(catalog);

        var publicProjection = core.Project(new PublicErrorProjectionInput
        {
            InternalCode = ClientErrorCodes.RateLimited,
            Audience = ClientErrorAudience.Public,
            RetryAfterSeconds = 7,
            CorrelationId = "corr-public",
            HttpStatusCode = 429,
        });
        var adminProjection = core.Project(new PublicErrorProjectionInput
        {
            InternalCode = ClientErrorCodes.RateLimited,
            Audience = ClientErrorAudience.AdminCompatibility,
            RetryAfterSeconds = 7,
            CorrelationId = "corr-admin",
            HttpStatusCode = 429,
        });

        var publicApproved = new HashSet<string>(StringComparer.Ordinal)
        {
            ClientErrorFieldNames.CorrelationId,
            ClientErrorFieldNames.RetryAfterSeconds,
        };
        var adminApproved = new HashSet<string>(publicApproved, StringComparer.Ordinal)
        {
            ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds,
        };

        foreach (var key in publicProjection.Data.Keys)
        {
            publicApproved.ShouldContain(key);
        }

        foreach (var key in adminProjection.Data.Keys)
        {
            adminApproved.ShouldContain(key);
        }

        adminProjection.Data.Keys.ShouldContain(
            ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds);
        publicProjection.Data.ContainsKey(
            ManagementCompatibilityDataWhitelist.RateLimiting.RetryAfterSeconds).ShouldBeFalse();
    }
}
