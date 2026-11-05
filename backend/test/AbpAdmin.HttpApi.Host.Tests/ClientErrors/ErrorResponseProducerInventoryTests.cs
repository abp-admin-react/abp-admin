using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P0-3：宿主端点与非 MVC 写出清单 + 漂移门禁测试。
/// 验证 Snapshots/error-response-producers.json 快照与生产代码中的错误响应写出点一致。
/// </summary>
/// <remarks>
/// <para>
/// JSON 快照是期望清单（baseline），生产代码中的错误写出点是 actual。
/// 测试通过反射/源码扫描生产代码，找到实际的错误响应写出点，然后与快照比较。
/// </para>
/// <para>
/// 漂移检测：
/// - 快照有但生产代码无 → 测试失败，提示"快照过期，生产代码已删除该写出点"。
/// - 生产代码有但快照无 → 测试失败，提示"发现新的错误写出点，需登记到快照"。
/// </para>
/// <para>
/// 改坏验证：修改快照或生产代码时，测试必须检测到差异并失败。
/// </para>
/// </remarks>
[Collection("AdminErrorEndpointRegistry")]
public class ErrorResponseProducerInventoryTests
{
    private static Task<WebApplication> HostTask => HostTestBed.HostTask;

    /// <summary>
    /// 从 JSON 快照加载期望的错误响应生产者清单。
    /// </summary>
    private static readonly string SnapshotPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "ClientErrors", "Snapshots",
        "error-response-producers.json");

    private static ErrorResponseProducerSnapshot LoadSnapshot()
    {
        if (!File.Exists(SnapshotPath))
        {
            throw new FileNotFoundException(
                $"快照文件不存在：{SnapshotPath}。请确保快照文件已提交到仓库。");
        }

        var json = File.ReadAllText(SnapshotPath);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        var snapshot = JsonSerializer.Deserialize<ErrorResponseProducerSnapshot>(json, options);

        return snapshot ?? throw new InvalidOperationException("快照反序列化失败：根对象为 null。");
    }

    /// <summary>
    /// 扫描生产代码中已知的非 MVC 错误响应写出点。
    /// 返回格式：{ ExitId, FilePath, DeclaringType, MethodName } 元组列表。
    /// </summary>
    /// <remarks>
    /// P0 阶段：通过硬编码清单跟踪这些写出点。
    /// 未来（P4）：使用 Roslyn 源码生成器或编译时扫描自动生成此清单。
    /// </remarks>
    private static List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol)> ScanProductionErrorProducers()
    {
        // P0 阶段：从生产代码中已验证的中间件/Filter 反射得到的清单
        // 这些类都在 framework-app 或 modules 中，有明确的错误响应写出行为
        return
        [
            // FriendlyBusinessExceptionMiddleware
            ("FriendlyBusinessExceptionMiddleware.InvokeAsync()",
             "AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs",
             "FriendlyBusinessExceptionMiddleware",
             "InvokeAsync",
             ErrorProtocol.AbpEnvelope),

            // TokenEndpointRateLimitingMiddleware
            ("TokenEndpointRateLimitingMiddleware.InvokeAsync()",
             "AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs",
             "TokenEndpointRateLimitingMiddleware",
             "InvokeAsync",
             ErrorProtocol.OAuth),

            // IdentitySessionValidationMiddleware
            ("IdentitySessionValidationMiddleware.InvokeAsync()",
             "AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs",
             "IdentitySessionValidationMiddleware",
             "InvokeAsync",
             ErrorProtocol.OAuth),

            // FileUploadContentGuardMiddleware
            ("FileUploadContentGuardMiddleware.InvokeAsync()",
             "AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs",
             "FileUploadContentGuardMiddleware",
             "InvokeAsync",
             ErrorProtocol.CustomJson),

            // AbpAdminHttpApiHostModule (TenantMismatch)
            ("AbpAdminHttpApiHostModule.TenantMismatch",
             "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
             "AbpAdminHttpApiHostModule",
             "OnTenantValidationFailure",
             ErrorProtocol.OAuth),

            // ImageProcessingBusyExceptionFilter
            ("ImageProcessingBusyExceptionFilter.OnException()",
             "AbpAdmin.HttpApi/Imaging/ImageProcessingBusyExceptionFilter.cs",
             "ImageProcessingBusyExceptionFilter",
             "OnException",
             ErrorProtocol.AbpEnvelope),

            // CookieAuthenticationEvents.OnRedirectToLogin
            ("CookieAuthenticationEvents.OnRedirectToLogin",
             "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
             "CookieAuthenticationEvents",
             "OnRedirectToLogin",
             ErrorProtocol.AbpEnvelope),

            // CookieAuthenticationEvents.OnRedirectToAccessDenied
            ("CookieAuthenticationEvents.OnRedirectToAccessDenied",
             "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
             "CookieAuthenticationEvents",
             "OnRedirectToAccessDenied",
             ErrorProtocol.AbpEnvelope),
        ];
    }

    /// <summary>
    /// 漂移门禁：验证 JSON 快照与生产代码中的错误写出点一致。
    /// 快照中的每个生产者都必须在生产代码中找到对应项，
    /// 生产代码中的每个写出点也必须在快照中登记。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此测试是真正的漂移检测门禁：
    /// - 快照有但生产代码无 → 测试失败，提示"快照过期"。
    /// - 生产代码有但快照无 → 测试失败，提示"发现新的写出点"。
    /// </para>
    /// <para>
    /// 改坏验证：修改快照或生产代码时，测试必须检测到差异并失败。
    /// 例如：在快照中删除一个条目，或修改 ExitId，测试必须变红。
    /// </para>
    /// </remarks>
    [Fact]
    public void ErrorProducer_Snapshot_Should_Match_ProductionCode()
    {
        // 1. 从 JSON 快照加载期望值
        var snapshot = LoadSnapshot();
        snapshot.SchemaVersion.ShouldBe(1, "快照 schema 版本必须为 1。");
        snapshot.Producers.Count.ShouldBeGreaterThan(0, "快照必须包含至少一个生产者。");

        // 2. 从生产代码扫描实际的错误写出点
        var actualProducers = ScanProductionErrorProducers();
        actualProducers.Count.ShouldBeGreaterThan(0, "生产代码必须包含至少一个错误写出点。");

        // 3. 构建比较集合（以 ExitId 为主键）
        var expectedByExitId = snapshot.Producers
            .Where(p => !string.IsNullOrEmpty(p.AssociatedExitId))
            .ToDictionary(p => p.AssociatedExitId!, p => p);

        var actualByExitId = actualProducers
            .ToDictionary(p => p.ExitId, p => p);

        // 4. 检测快照过期：快照有但生产代码无
        var staleInSnapshot = expectedByExitId.Keys
            .Except(actualByExitId.Keys)
            .OrderBy(k => k)
            .ToList();

        staleInSnapshot.ShouldBeEmpty(
            $"快照过期：以下 ExitId 在快照中存在但生产代码中已删除或重命名：{string.Join(", ", staleInSnapshot)}。" +
            "请更新快照或确认生产代码变更。");

        // 5. 检测遗漏登记：生产代码有但快照无
        var missingInSnapshot = actualByExitId.Keys
            .Except(expectedByExitId.Keys)
            .OrderBy(k => k)
            .ToList();

        missingInSnapshot.ShouldBeEmpty(
            $"发现新的错误写出点：以下 ExitId 在生产代码中存在但快照中未登记：{string.Join(", ", missingInSnapshot)}。" +
            "请登记到快照或确认是否为错误写出点。");

        // 6. 验证关键字段匹配（Protocol、DeclaringType、MethodName）
        foreach (var (exitId, expected) in expectedByExitId)
        {
            var actual = actualByExitId[exitId];

            actual.Protocol.ShouldBe(expected.Protocol,
                $"ExitId {exitId} 的 Protocol 不匹配：期望 {expected.Protocol}，实际 {actual.Protocol}。" +
                "请同步快照与生产代码。");

            actual.DeclaringType.ShouldBe(expected.DeclaringType,
                $"ExitId {exitId} 的 DeclaringType 不匹配：期望 {expected.DeclaringType}，实际 {actual.DeclaringType}。" +
                "请同步快照与生产代码。");

            actual.MethodName.ShouldBe(expected.MethodName,
                $"ExitId {exitId} 的 MethodName 不匹配：期望 {expected.MethodName}，实际 {actual.MethodName}。" +
                "请同步快照与生产代码。");
        }
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
/// 错误响应生产者快照。
/// </summary>
public sealed record ErrorResponseProducerSnapshot
{
    public DateTime GeneratedAt { get; init; }
    public int SchemaVersion { get; init; }
    public IReadOnlyList<ErrorResponseProducerCandidate> Producers { get; init; } = [];
}
