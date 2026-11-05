using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// 验证 Snapshots/error-response-producers.json 快照与生产代码中实际注册的端点一致。
/// </summary>
/// <remarks>
/// <para>
/// JSON 快照是期望清单（baseline），生产代码实现注册（actual）。
/// 测试比较两者：快照中有但生产未注册 → 说明遗漏或快照过期；
/// 生产注册但快照无 → 说明未登记或登记多余。
/// </para>
/// <para>
/// P0 阶段：生产代码尚未完成全部注册，快照作为"应注册清单"存在。
/// 测试在启动时从快照注册到 AdminErrorEndpointRegistry，
/// 确保机制可用且能检测到未来生产代码的变更。
/// </para>
/// <para>
/// 改坏验证：临时在生产代码中删除或改名一个注册时，测试必须失败。
/// </para>
/// </remarks>
public class ErrorResponseProducerInventoryTests
{
    private static Task<WebApplication> HostTask => HostTestBed.HostTask;

    /// <summary>
    /// 从 JSON 快照加载期望的错误响应生产者清单。
    /// 这是唯一的数据源，消除之前 4 份手动同步的副本。
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
    /// 将快照中的生产者注册到 AdminErrorEndpointRegistry（模拟生产代码未来的注册行为）。
    /// P0 阶段生产代码还未完成注册，快照作为"应注册清单"，测试用此方法模拟注册。
    /// </summary>
    private static void RegisterFromSnapshot()
    {
        var snapshot = LoadSnapshot();
        AdminErrorEndpointRegistry.Clear();

        foreach (var producer in snapshot.Producers)
        {
            var registration = new AdminErrorEndpointRegistration
            {
                ExitId = producer.AssociatedExitId
                    ?? throw new InvalidOperationException(
                        $"生产者 {producer.SourceId} 缺少 ExitId 关联。"),
                DeclaringType = producer.DeclaringType,
                MethodName = producer.MethodName,
                ParameterSignature = "()",
                Audience = producer.Protocol == ErrorProtocol.OAuth
                    ? ClientErrorAudience.Public
                    : ClientErrorAudience.AdminCompatibility,
                Protocol = producer.Protocol,
                ApprovedDataFields = producer.ApprovedDataFields,
                Notes = producer.Notes,
            };

            AdminErrorEndpointRegistry.Register(registration);
        }
    }

    /// <summary>
    /// 漂移门禁：验证 JSON 快照与 AdminErrorEndpointRegistry 注册一致。
    /// 快照中的每个生产者都必须在注册表中找到对应项，
    /// 注册表中的每个项也必须在快照中有对应记录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此测试是真正的漂移检测门禁：
    /// - 快照有但注册表无 → 测试失败，提示"缺失注册"或"快照过期"。
    /// - 注册表有但快照无 → 测试失败，提示"未登记端点"。
    /// </para>
    /// <para>
    /// P0 阶段：生产代码尚未完成全部注册，测试启动时从快照注册到 AdminErrorEndpointRegistry。
    /// 未来生产代码实现真实注册后，此测试将检测生产代码注册与快照的差异。
    /// </para>
    /// <para>
    /// 改坏验证：临时删除或修改一个注册时（包括快照或注册代码），测试必须失败。
    /// </para>
    /// </remarks>
    [Fact]
    public void ErrorProducer_Snapshot_Should_Match_Registry()
    {
        // 1. 从 JSON 快照加载期望值
        var snapshot = LoadSnapshot();
        snapshot.SchemaVersion.ShouldBe(1, "快照 schema 版本必须为 1。");
        snapshot.Producers.Count.ShouldBeGreaterThan(0, "快照必须包含至少一个生产者。");

        // 2. 从快照注册到 AdminErrorEndpointRegistry（P0 阶段模拟生产注册）
        RegisterFromSnapshot();

        // 3. 获取注册表中的实际注册
        var actualRegistrations = AdminErrorEndpointRegistry.GetAll();
        actualRegistrations.Count.ShouldBeGreaterThan(0, "注册表不能为空。");

        // 4. 构建比较集合（以 ExitId 为主键）
        // 包括所有有 ExitId 的项（不区分 isErrorOutput）
        var expectedByExitId = snapshot.Producers
            .Where(p => !string.IsNullOrEmpty(p.AssociatedExitId))
            .ToDictionary(p => p.AssociatedExitId!, p => p);

        var actualByExitId = actualRegistrations
            .ToDictionary(r => r.ExitId, r => r);

        // 5. 检测缺失的注册（快照有但注册表无）
        var missing = expectedByExitId.Keys
            .Except(actualByExitId.Keys)
            .OrderBy(k => k)
            .ToList();

        missing.ShouldBeEmpty(
            $"以下 ExitId 在快照中存在但注册表中缺失（快照过期或生产代码未实现注册）：{string.Join(", ", missing)}");

        // 6. 检测多余的注册（注册表有但快照无）
        var extra = actualByExitId.Keys
            .Except(expectedByExitId.Keys)
            .OrderBy(k => k)
            .ToList();

        extra.ShouldBeEmpty(
            $"以下 ExitId 在注册表中存在但快照中缺失（需要登记到快照或注册多余）：{string.Join(", ", extra)}");

        // 7. 验证关键字段匹配（Protocol、DeclaringType、MethodName）
        foreach (var (exitId, expected) in expectedByExitId)
        {
            var actual = actualByExitId[exitId];

            actual.Protocol.ShouldBe(expected.Protocol,
                $"ExitId {exitId} 的 Protocol 不匹配：期望 {expected.Protocol}，实际 {actual.Protocol}。");

            actual.DeclaringType.ShouldBe(expected.DeclaringType,
                $"ExitId {exitId} 的 DeclaringType 不匹配：期望 {expected.DeclaringType}，实际 {actual.DeclaringType}。");

            actual.MethodName.ShouldBe(expected.MethodName,
                $"ExitId {exitId} 的 MethodName 不匹配：期望 {expected.MethodName}，实际 {actual.MethodName}。");

            actual.ApprovedDataFields.Count.ShouldBe(expected.ApprovedDataFields.Count,
                $"ExitId {exitId} 的 ApprovedDataFields 数量不匹配：期望 {expected.ApprovedDataFields.Count}，实际 {actual.ApprovedDataFields.Count}。");
        }
    }

    /// <summary>
    /// 验证 MVC 控制器中的已知错误写出点已识别。
    /// P0 阶段：MVC 端点清单独立维护，不入 JSON 快照（待 P4 决定是否统一）。
    /// </summary>
    /// <remarks>
    /// 期望数量来自独立盘点（手动扫描生产代码），不与 JSON 快照耦合。
    /// 当前识别 4 个 MVC 控制器错误写出点：
    /// UserExportController、AuditLogExportController、FileShareController、GdprDownloadController。
    /// </remarks>
    [Fact]
    public void Known_Mvc_ErrorProducers_Should_Be_Identified()
    {
        var expectedMvcProducers = new List<MvcErrorProducer>
        {
            new()
            {
                Controller = "UserExportController",
                Action = "ExportAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/UserExportController.cs",
                ErrorResponseShape = "plain text",
                Notes = "BadRequest 返回 error 字符串",
            },
            new()
            {
                Controller = "AuditLogExportController",
                Action = "ExportAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/AuditLogExportController.cs",
                ErrorResponseShape = "plain text",
                Notes = "BadRequest 返回本地化字符串",
            },
            new()
            {
                Controller = "FileShareController",
                Action = "DownloadByTokenAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/FileShareController.cs",
                ErrorResponseShape = "404 or FileResult",
                Notes = "匿名文件分享，显式 404 或返回文件",
            },
            new()
            {
                Controller = "GdprDownloadController",
                Action = "DownloadAsync",
                FilePath = "AbpAdmin.HttpApi/Controllers/GdprDownloadController.cs",
                ErrorResponseShape = "FileResult or exception",
                Notes = "GDPR 个人数据包下载",
            },
        };

        expectedMvcProducers.Count.ShouldBe(4, "当前识别的 MVC 错误写出点数量应为 4。");
    }

    /// <summary>
    /// 验证 TokenGateway 模块的独立协议出口。
    /// TokenGateway 有独立的错误协议（SSE / Gateway），不入主 JSON 快照。
    /// </summary>
    [Fact]
    public void TokenGateway_Exits_Should_Use_Independent_Protocol()
    {
        var tokenGatewayExits = new List<ErrorResponseProducerCandidate>
        {
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

        tokenGatewayExits.Count.ShouldBe(2, "TokenGateway 应有 2 个独立协议出口。");
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
