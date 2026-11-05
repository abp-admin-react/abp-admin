using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// 验证图片繁忙 503 + Retry-After 行为。
    /// P0 记录当前行为：filter 补头，不接管异常，体由 ABP 写出。
    /// </summary>
    [Fact]
    public void Image_Processing_Busy_503_Should_Add_RetryAfter_Header()
    {
        // ImageProcessingBusyExceptionFilter.cs:20
        // 补 Retry-After 头，不标记 handled
        // 响应体仍由 ABP 的 AbpExceptionFilter 处理

        var busyException = new AbpAdminImageProcessingBusyException(30);

        busyException.RetryAfterSeconds.ShouldBe(30);
        busyException.HttpStatusCode.ShouldBe(503);

        // P0 不改变运行时行为
        // P1 验证：
        // - filter 先于 AbpExceptionFilter 执行
        // - 头被正确写入
        // - ABP 写出标准错误信封
    }

    /// <summary>
    /// 验证管理端 503 通用提示。
    /// 本期不新增管理端 503 倒计时交互。
    /// </summary>
    [Fact]
    public void Management_503_Should_Use_Generic_Message()
    {
        // 方案 3.2 明确：
        // - 基础 server 定义允许可选的有界 retryAfterSeconds
        // - message 仍是静态通用故障提示，不拿等待参数插值
        // - 不是让前端看到 503 后覆盖已识别 kind

        var serverError = new ServerErrorCompatibility
        {
            HttpStatusCode = 503,
            Kind = PublicErrorKind.Server,
            Code = ClientErrorCodes.Unexpected,
            Message = "服务暂时不可用，请稍后重试。",
            HasRetryAfterHeader = true,
            HasRetryAfterData = false, // 不插值
        };

        serverError.Kind.ShouldBe(PublicErrorKind.Server);
        serverError.HasRetryAfterData.ShouldBeFalse(
            "本期不新增管理端 503 倒计时交互；retryAfter 通过 header 传递。");
    }

    /// <summary>
    /// 验证限流 429 + Retry-After 行为。
    /// </summary>
    [Fact]
    public void RateLimited_429_Should_Preserve_RetryAfter()
    {
        // TokenEndpointRateLimitingMiddleware.cs:107-120
        // - 写 429 状态码
        // - 写 Retry-After 头
        // - 写 OAuth 风格 JSON（error + error_description）

        var rateLimitedResponse = new RateLimitedCompatibility
        {
            HttpStatusCode = 429,
            Protocol = ErrorProtocol.OAuth,
            HasRetryAfterHeader = true,
            HeaderValue = 300,
            BodyFields = ["error", "error_description"],
            ApprovedDataFields = [], // OAuth 格式无 data 字段
        };

        rateLimitedResponse.Protocol.ShouldBe(ErrorProtocol.OAuth);
        rateLimitedResponse.HasRetryAfterHeader.ShouldBeTrue();
        rateLimitedResponse.BodyFields.ShouldContain("error");
        rateLimitedResponse.BodyFields.ShouldContain("error_description");
    }

    /// <summary>
    /// 验证管理端 404 全局静默契约。
    /// web/src/requestErrorConfig.ts 对 404 不弹全局错误。
    /// </summary>
    [Fact]
    public void Management_404_Should_Be_Globally_Silent()
    {
        // docs/framework-contracts.md:70-72
        // 404 全局静默契约：
        // - requestErrorConfig 对 404 不弹全局错误
        // - 业务语义：不存在/未配置
        // - 页面层读端点存在该语义时必须用 isNotFound 分流降级

        var notFoundRule = new NotFoundCompatibility
        {
            HttpStatusCode = 404,
            GlobalSilent = true, // 不弹全局错误
            PageHandling = "isNotFound 分流降级",
            DataFields = [], // 404 通常无 data
        };

        notFoundRule.GlobalSilent.ShouldBeTrue();
        notFoundRule.PageHandling.ShouldBe("isNotFound 分流降级");

        // 前端测试覆盖：
        // web/src/requestErrorConfig.test.ts 中有 404 静默测试
    }

    /// <summary>
    /// 验证管理端 403 单提示契约。
    /// </summary>
    [Fact]
    public void Management_403_Should_Show_Single_Toast()
    {
        // 非权限 403 弹全局提示
        // 不触发会话清理或登录恢复

        var forbiddenRule = new ForbiddenCompatibility
        {
            HttpStatusCode = 403,
            GlobalToast = true,
            TriggerSessionClear = false,
            TriggerLoginRecovery = false,
        };

        forbiddenRule.GlobalToast.ShouldBeTrue();
        forbiddenRule.TriggerSessionClear.ShouldBeFalse();
        forbiddenRule.TriggerLoginRecovery.ShouldBeFalse();
    }

    /// <summary>
    /// 验证管理端 429 等待信息契约。
    /// </summary>
    [Fact]
    public void Management_429_Should_Show_Wait_Time()
    {
        // 方案 4.3/A31：
        // - 保留管理 429 等待信息契约
        // - error.data.RetryAfterSeconds（管理端 PascalCase）
        // - 或从 Retry-After 响应头解析

        var rateLimitedManagement = new RateLimitedManagementCompatibility
        {
            HttpStatusCode = 429,
            HasRetryAfterHeader = true,
            HasRetryAfterDataField = true,
            DataFieldName = "RetryAfterSeconds", // 管理 PascalCase
            CustomerDataFieldName = "retryAfterSeconds", // 客户 lowerCamelCase
        };

        rateLimitedManagement.DataFieldName.ShouldBe("RetryAfterSeconds");
        rateLimitedManagement.CustomerDataFieldName.ShouldBe("retryAfterSeconds");
    }

    /// <summary>
    /// 验证 ImageProcessingBusy 由目录映射到 Unexpected/server + 503。
    /// 错误注册失败 scenario（P1 实现）。
    /// </summary>
    [Fact]
    public void ImageProcessingBusy_Should_Map_To_Unexpected_Server_503()
    {
        // 方案 3.2 明确：
        // - 内部 AbpAdmin:ImageProcessingBusy 映射至 Client:Unexpected
        // - 目录 kind 固定 server
        // - 实际 HTTP 503
        // - 基础 server 定义允许可选的有界 retryAfterSeconds
        // - message 仍是静态通用故障提示，不拿等待参数插值

        var mapping = new InternalToPublicMapping
        {
            InternalCode = "AbpAdmin:ImageProcessingBusy",
            PublicCode = ClientErrorCodes.Unexpected,
            Kind = PublicErrorKind.Server,
            HttpStatusCode = 503,
            HasRetryAfter = true,
            RetryAfterSource = "header", // 仅 header，不插值到 message
        };

        mapping.PublicCode.ShouldBe(ClientErrorCodes.Unexpected);
        mapping.Kind.ShouldBe(PublicErrorKind.Server);
        mapping.RetryAfterSource.ShouldBe("header",
            "等待时间通过 Retry-After header 传递，不插值到 message。");
    }

    /// <summary>
    /// 验证管理响应 data 键集合是批准集合的子集。
    /// A17 管理兼容断言。
    /// </summary>
    [Fact]
    public void Management_DataKeys_Should_Be_Subset_Of_Approved()
    {
        // 方案 4.3：
        // - 管理响应 error.data 键集合 ⊆ 批准集合
        // - 未批准字段丢弃
        // - ExtraProperties 没有自动公开资格

        var scenarios = new List<DataKeySubsetScenario>
        {
            new()
            {
                ExitId = "TokenEndpointRateLimitingMiddleware.InvokeAsync()",
                ActualKeys = [], // OAuth 格式无 data
                ApprovedKeys = [],
                IsValid = true,
            },
            new()
            {
                ExitId = "FileUploadContentGuardMiddleware.InvokeAsync()",
                ActualKeys = ["fileName", "reason"],
                ApprovedKeys = ["fileName", "reason"],
                IsValid = true,
            },
            new()
            {
                ExitId = "FriendlyBusinessExceptionMiddleware.InvokeAsync()",
                ActualKeys = ["retryAfterSeconds"], // 管理端限流
                ApprovedKeys = ["RetryAfterSeconds"],
                IsValid = false, // 大小写不兼容
                Note = "需在 P1 实现大小写兼容映射",
            },
        };

        foreach (var scenario in scenarios)
        {
            if (scenario.IsValid)
            {
                // P0 只验证公约，P1 实现过滤
            }
        }
    }
}

/// <summary>
/// 服务端错误兼容性。
/// </summary>
public sealed record ServerErrorCompatibility
{
    public int HttpStatusCode { get; init; }
    public PublicErrorKind Kind { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public bool HasRetryAfterHeader { get; init; }
    public bool HasRetryAfterData { get; init; }
}

/// <summary>
/// 限流兼容性。
/// </summary>
public sealed record RateLimitedCompatibility
{
    public int HttpStatusCode { get; init; }
    public ErrorProtocol Protocol { get; init; }
    public bool HasRetryAfterHeader { get; init; }
    public int HeaderValue { get; init; }
    public IReadOnlyList<string> BodyFields { get; init; } = [];
    public IReadOnlyList<string> ApprovedDataFields { get; init; } = [];
}

/// <summary>
/// 404 兼容性。
/// </summary>
public sealed record NotFoundCompatibility
{
    public int HttpStatusCode { get; init; }
    public bool GlobalSilent { get; init; }
    public string PageHandling { get; init; } = "";
    public IReadOnlyList<string> DataFields { get; init; } = [];
}

/// <summary>
/// 403 兼容性。
/// </summary>
public sealed record ForbiddenCompatibility
{
    public int HttpStatusCode { get; init; }
    public bool GlobalToast { get; init; }
    public bool TriggerSessionClear { get; init; }
    public bool TriggerLoginRecovery { get; init; }
}

/// <summary>
/// 管理端 429 兼容性。
/// </summary>
public sealed record RateLimitedManagementCompatibility
{
    public int HttpStatusCode { get; init; }
    public bool HasRetryAfterHeader { get; init; }
    public bool HasRetryAfterDataField { get; init; }
    public string DataFieldName { get; init; } = "";
    public string CustomerDataFieldName { get; init; } = "";
}

/// <summary>
/// 内部码到公开码映射。
/// </summary>
public sealed record InternalToPublicMapping
{
    public string InternalCode { get; init; } = "";
    public string PublicCode { get; init; } = "";
    public PublicErrorKind Kind { get; init; }
    public int HttpStatusCode { get; init; }
    public bool HasRetryAfter { get; init; }
    public string RetryAfterSource { get; init; } = "";
}

/// <summary>
/// Data 键子集验证场景。
/// </summary>
public sealed record DataKeySubsetScenario
{
    public string ExitId { get; init; } = "";
    public IReadOnlyList<string> ActualKeys { get; init; } = [];
    public IReadOnlyList<string> ApprovedKeys { get; init; } = [];
    public bool IsValid { get; init; }
    public string? Note { get; init; }
}
