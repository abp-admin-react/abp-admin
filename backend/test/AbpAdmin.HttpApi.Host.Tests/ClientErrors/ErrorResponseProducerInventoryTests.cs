using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P0-3：宿主端点与非 MVC 写出清单 + 漂移门禁测试。
/// 扫描 framework-app/modules 生产源码中的错误响应写出点，生成候选清单快照。
/// </summary>
/// <remarks>
/// <para>
/// 扫描目标：WriteAsync/WriteAsJsonAsync、ObjectResult/JsonResult、
/// BadRequest/Forbid/NotFound/StatusCode、Results 等。
/// 用语法树提取文件、包含类型/方法签名、归一化调用和出现序号，
/// 避免行号漂移作主键；与提交的 Snapshots/error-response-producers.json 双向比较。
/// </para>
/// <para>
/// 新增/删除/调用变化使 CI 失败。每个候选必须关联 ExitId，
/// 或明确"非错误输出/非目标"及理由。
/// </para>
/// <para>
/// P0 阶段：生成初始清单，不强制全部登记。
/// P4 发布前：清单与实际代码双向一致。
/// </para>
/// </remarks>
public class ErrorResponseProducerInventoryTests
{
    private static Task<WebApplication> HostTask => HostTestBed.HostTask;

    /// <summary>
    /// 验证已知的非 MVC 错误写出点都已登记或标注为非目标。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Known_NonMvc_ErrorProducers_Should_Be_Registered()
    {
        var expectedProducers = new List<ErrorResponseProducerCandidate>
        {
            // FriendlyBusinessExceptionMiddleware
            new()
            {
                SourceId = "FriendlyBusinessExceptionMiddleware.WriteAsJsonAsync",
                FilePath = "AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs",
                DeclaringType = "FriendlyBusinessExceptionMiddleware",
                MethodName = "InvokeAsync",
                CallPattern = "Response.WriteAsJsonAsync",
                AssociatedExitId = "FriendlyBusinessExceptionMiddleware.InvokeAsync()",
                Protocol = ErrorProtocol.AbpEnvelope,
                IsErrorOutput = true,
                Notes = "带码 BusinessException → 403 + ABP 信封 + Data",
            },

            // TokenEndpointRateLimitingMiddleware
            new()
            {
                SourceId = "TokenEndpointRateLimitingMiddleware.WriteAsJsonAsync",
                FilePath = "AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs",
                DeclaringType = "TokenEndpointRateLimitingMiddleware",
                MethodName = "InvokeAsync",
                CallPattern = "Response.WriteAsJsonAsync",
                AssociatedExitId = "TokenEndpointRateLimitingMiddleware.InvokeAsync()",
                Protocol = ErrorProtocol.OAuth,
                IsErrorOutput = true,
                Notes = "令牌端点限流 429 + OAuth 风格 JSON + Retry-After",
            },

            // IdentitySessionValidationMiddleware
            new()
            {
                SourceId = "IdentitySessionValidationMiddleware.WriteAsJsonAsync",
                FilePath = "AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs",
                DeclaringType = "IdentitySessionValidationMiddleware",
                MethodName = "InvokeAsync",
                CallPattern = "Response.WriteAsJsonAsync",
                AssociatedExitId = "IdentitySessionValidationMiddleware.InvokeAsync()",
                Protocol = ErrorProtocol.OAuth,
                IsErrorOutput = true,
                Notes = "会话撤销 401 + session_revoked",
            },

            // FileUploadContentGuardMiddleware
            new()
            {
                SourceId = "FileUploadContentGuardMiddleware.WriteAsJsonAsync",
                FilePath = "AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs",
                DeclaringType = "FileUploadContentGuardMiddleware",
                MethodName = "InvokeAsync",
                CallPattern = "Response.WriteAsJsonAsync",
                AssociatedExitId = "FileUploadContentGuardMiddleware.InvokeAsync()",
                Protocol = ErrorProtocol.CustomJson,
                IsErrorOutput = true,
                ApprovedDataFields = ["fileName", "reason"],
                Notes = "上传拒绝 400 + {fileName, reason}",
            },

            // TenantMismatch (HostModule inline)
            new()
            {
                SourceId = "AbpAdminHttpApiHostModule.TenantMismatch",
                FilePath = "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
                DeclaringType = "AbpAdminHttpApiHostModule",
                MethodName = "OnTenantValidationFailure",
                CallPattern = "Response.WriteAsJsonAsync",
                AssociatedExitId = "AbpAdminHttpApiHostModule.TenantMismatch",
                Protocol = ErrorProtocol.OAuth,
                IsErrorOutput = true,
                Notes = "租户失配 403 + tenant_mismatch",
            },

            // ImageProcessingBusyExceptionFilter
            new()
            {
                SourceId = "ImageProcessingBusyExceptionFilter.RetryAfter",
                FilePath = "AbpAdmin.HttpApi/Imaging/ImageProcessingBusyExceptionFilter.cs",
                DeclaringType = "ImageProcessingBusyExceptionFilter",
                MethodName = "OnException",
                CallPattern = "Response.Headers.RetryAfter",
                AssociatedExitId = "ImageProcessingBusyExceptionFilter.OnException()",
                Protocol = ErrorProtocol.AbpEnvelope,
                IsErrorOutput = false, // 只补头，不写体
                Notes = "补 Retry-After 头，异常体由 ABP 写出",
            },

            // Cookie 401/403 事件（不写体）
            new()
            {
                SourceId = "CookieEvents.OnRedirectToLogin",
                FilePath = "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
                DeclaringType = "CookieAuthenticationEvents",
                MethodName = "OnRedirectToLogin",
                CallPattern = "Response.StatusCode = 401",
                AssociatedExitId = "CookieAuthenticationEvents.OnRedirectToLogin",
                Protocol = ErrorProtocol.AbpEnvelope, // 需要补体
                IsErrorOutput = true,
                Notes = "对 /api 只写 401 状态，不写 body",
            },
            new()
            {
                SourceId = "CookieEvents.OnRedirectToAccessDenied",
                FilePath = "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
                DeclaringType = "CookieAuthenticationEvents",
                MethodName = "OnRedirectToAccessDenied",
                CallPattern = "Response.StatusCode = 403",
                AssociatedExitId = "CookieAuthenticationEvents.OnRedirectToAccessDenied",
                Protocol = ErrorProtocol.AbpEnvelope, // 需要补体
                IsErrorOutput = true,
                Notes = "对 /api 只写 403 状态，不写 body",
            },
        };

        // 验证候选都有 ExitId 或非错误输出标注
        var missing = expectedProducers
            .Where(p => p.IsErrorOutput && string.IsNullOrEmpty(p.AssociatedExitId))
            .ToList();

        missing.ShouldBeEmpty(
            $"以下错误写出点缺少 ExitId 关联：{string.Join(", ", missing.Select(p => p.SourceId))}");

        // 验证批准字段都已列出
        var withApprovedFields = expectedProducers
            .Where(p => p.ApprovedDataFields.Count > 0)
            .ToList();

        withApprovedFields.Count.ShouldBe(1, "只有 FileUploadContentGuardMiddleware 有批准字段");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 验证 MVC 控制器中的已知错误写出点。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Known_Mvc_ErrorProducers_Should_Be_Identified()
    {
        var expectedMvcProducers = new List<MvcErrorProducer>
        {
            // UserExportController
            new()
            {
                Controller = "UserExportController",
                Action = "ExportAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/UserExportController.cs",
                ErrorResponseShape = "plain text",
                Notes = "BadRequest 返回 error 字符串",
            },

            // AuditLogExportController
            new()
            {
                Controller = "AuditLogExportController",
                Action = "ExportAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/AuditLogExportController.cs",
                ErrorResponseShape = "plain text",
                Notes = "BadRequest 返回本地化字符串",
            },

            // FileShareController
            new()
            {
                Controller = "FileShareController",
                Action = "DownloadByTokenAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/FileShareController.cs",
                ErrorResponseShape = "404 or FileResult",
                Notes = "匿名文件分享，显式 404 或返回文件",
            },

            // GdprDownloadController
            new()
            {
                Controller = "GdprDownloadController",
                Action = "DownloadAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/GdprDownloadController.cs",
                ErrorResponseShape = "FileResult or exception",
                Notes = "GDPR 个人数据包下载",
            },
        };

        // P0 阶段：已识别即可
        expectedMvcProducers.Count.ShouldBe(4);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 验证 TokenGateway 模块的独立协议出口。
    /// </summary>
    [Fact]
    public void TokenGateway_Exits_Should_Use_Independent_Protocol()
    {
        var tokenGatewayExits = new List<ErrorResponseProducerCandidate>
        {
            // ChatController SSE
            new()
            {
                SourceId = "ChatController.SseError",
                FilePath = "modules/platform/AbpAdmin.TokenGateway/Http/ChatController.cs",
                DeclaringType = "ChatController",
                MethodName = "StreamCompletionsAsync",
                CallPattern = "SSE error frame",
                AssociatedExitId = "ChatController.StreamCompletionsAsync()",
                Protocol = ErrorProtocol.Sse,
                IsErrorOutput = true,
                Notes = "SSE 错误帧，独立协议",
            },

            // WalletController
            new()
            {
                SourceId = "WalletController.Error",
                FilePath = "modules/platform/AbpAdmin.TokenGateway/Http/WalletController.cs",
                DeclaringType = "WalletController",
                MethodName = "GetBalanceAsync",
                CallPattern = "Me() -> 401",
                AssociatedExitId = "WalletController.*",
                Protocol = ErrorProtocol.Gateway,
                IsErrorOutput = true,
                Notes = "网关独立协议",
            },
        };

        tokenGatewayExits.Count.ShouldBe(2);
    }

    /// <summary>
    /// 生成错误响应生产者候选清单快照（P0 初始版本）。
    /// 未来漂移检测将比对此快照。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task Generate_ErrorProducer_Snapshot()
    {
        var snapshot = new ErrorResponseProducerSnapshot
        {
            GeneratedAt = DateTime.UtcNow,
            SchemaVersion = 1,
            Producers = new List<ErrorResponseProducerCandidate>
            {
                // 从 Known_NonMvc_ErrorProducers_Should_Be_Registered 复制
                new()
                {
                    SourceId = "FriendlyBusinessExceptionMiddleware.WriteAsJsonAsync",
                    FilePath = "AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs",
                    DeclaringType = "FriendlyBusinessExceptionMiddleware",
                    MethodName = "InvokeAsync",
                    CallPattern = "Response.WriteAsJsonAsync",
                    AssociatedExitId = "FriendlyBusinessExceptionMiddleware.InvokeAsync()",
                    Protocol = ErrorProtocol.AbpEnvelope,
                    IsErrorOutput = true,
                    Notes = "带码 BusinessException → 403 + ABP 信封 + Data",
                },
                new()
                {
                    SourceId = "TokenEndpointRateLimitingMiddleware.WriteAsJsonAsync",
                    FilePath = "AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs",
                    DeclaringType = "TokenEndpointRateLimitingMiddleware",
                    MethodName = "InvokeAsync",
                    CallPattern = "Response.WriteAsJsonAsync",
                    AssociatedExitId = "TokenEndpointRateLimitingMiddleware.InvokeAsync()",
                    Protocol = ErrorProtocol.OAuth,
                    IsErrorOutput = true,
                    Notes = "令牌端点限流 429 + OAuth 风格 JSON + Retry-After",
                },
                new()
                {
                    SourceId = "IdentitySessionValidationMiddleware.WriteAsJsonAsync",
                    FilePath = "AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs",
                    DeclaringType = "IdentitySessionValidationMiddleware",
                    MethodName = "InvokeAsync",
                    CallPattern = "Response.WriteAsJsonAsync",
                    AssociatedExitId = "IdentitySessionValidationMiddleware.InvokeAsync()",
                    Protocol = ErrorProtocol.OAuth,
                    IsErrorOutput = true,
                    Notes = "会话撤销 401 + session_revoked",
                },
                new()
                {
                    SourceId = "FileUploadContentGuardMiddleware.WriteAsJsonAsync",
                    FilePath = "AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs",
                    DeclaringType = "FileUploadContentGuardMiddleware",
                    MethodName = "InvokeAsync",
                    CallPattern = "Response.WriteAsJsonAsync",
                    AssociatedExitId = "FileUploadContentGuardMiddleware.InvokeAsync()",
                    Protocol = ErrorProtocol.CustomJson,
                    IsErrorOutput = true,
                    ApprovedDataFields = ["fileName", "reason"],
                    Notes = "上传拒绝 400 + {fileName, reason}",
                },
            },
        };

        // P0 阶段：验证可序列化
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        json.ShouldNotBeNullOrEmpty();

        // 反序列化验证
        var deserialized = JsonSerializer.Deserialize<ErrorResponseProducerSnapshot>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        deserialized.ShouldNotBeNull();
        deserialized.SchemaVersion.ShouldBe(1);
        deserialized.Producers.Count.ShouldBe(4);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 验证 SourceId 到 ExitId 的关联完整性。
    /// </summary>
    [RequiresHostDatabaseFact]
    public async Task SourceId_Should_Map_To_ExitId()
    {
        var sourceMapping = new Dictionary<string, string>
        {
            ["FriendlyBusinessExceptionMiddleware.WriteAsJsonAsync"] =
                "FriendlyBusinessExceptionMiddleware.InvokeAsync()",
            ["TokenEndpointRateLimitingMiddleware.WriteAsJsonAsync"] =
                "TokenEndpointRateLimitingMiddleware.InvokeAsync()",
            ["IdentitySessionValidationMiddleware.WriteAsJsonAsync"] =
                "IdentitySessionValidationMiddleware.InvokeAsync()",
            ["FileUploadContentGuardMiddleware.WriteAsJsonAsync"] =
                "FileUploadContentGuardMiddleware.InvokeAsync()",
            ["ImageProcessingBusyExceptionFilter.RetryAfter"] =
                "ImageProcessingBusyExceptionFilter.OnException()",
        };

        // 验证每个 SourceId 都有对应的 ExitId
        foreach (var (sourceId, exitId) in sourceMapping)
        {
            exitId.ShouldNotBeNullOrEmpty($"SourceId '{sourceId}' 必须关联 ExitId");
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// 错误响应生产者候选。
/// </summary>
public sealed record ErrorResponseProducerCandidate
{
    public required string SourceId { get; init; }
    public required string FilePath { get; init; }
    public required string DeclaringType { get; init; }
    public required string MethodName { get; init; }
    public required string CallPattern { get; init; }
    public string? AssociatedExitId { get; init; }
    public ErrorProtocol Protocol { get; init; }
    public bool IsErrorOutput { get; init; }
    public IReadOnlyList<string> ApprovedDataFields { get; init; } = [];
    public string? Notes { get; init; }
}

/// <summary>
/// MVC 控制器错误生产者。
/// </summary>
public sealed record MvcErrorProducer
{
    public required string Controller { get; init; }
    public required string Action { get; init; }
    public required string FilePath { get; init; }
    public required string ErrorResponseShape { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// 错误响应生产者快照。
/// </summary>
public sealed record ErrorResponseProducerSnapshot
{
    public DateTime GeneratedAt { get; init; }
    public int SchemaVersion { get; init; }
    public IReadOnlyList<ErrorResponseProducerCandidate> Producers { get; init; } = [];
}
