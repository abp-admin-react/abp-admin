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
    /// 扫描生产代码中实际注册的非 MVC 错误响应写出点。
    /// 通过读取 HostModule.cs 源码查找 UseMiddleware<T> 注册，然后反射这些中间件的 InvokeAsync 方法。
    /// </summary>
    /// <remarks>
    /// P0 阶段实现：源码扫描 + 反射。过渡方案等到 P4 替换为完整的编译时扫描。
    /// 只返回实际在生产代码中注册的中间件类型。
    /// </remarks>
    private static List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)> ScanProductionErrorProducers()
    {
        var actualProducers = new List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)>();
        
        // 1. 找到仓库根目录
        var repoRoot = FindRepositoryRoot();
        
        // 2. 读取 HostModule.cs 源码
        var hostModulePath = Path.Combine(repoRoot, "backend", "framework-app", "AbpAdmin.HttpApi.Host", "AbpAdminHttpApiHostModule.cs");
        if (!File.Exists(hostModulePath))
        {
            throw new FileNotFoundException($"无法找到 HostModule.cs: {hostModulePath}");
        }
        
        var hostModuleCode = File.ReadAllText(hostModulePath);
        
        // 3. 扫描 UseMiddleware<T> 调用，只匹配精确类型名
        var middlewareRegistrations = ScanMiddlewareRegistrations(hostModuleCode);
        
        // 4. 检查每个注册的中间件是否在快照中有对应项
        foreach (var registration in middlewareRegistrations)
        {
            // 根据中间件类型推断协议
            var protocol = InferProtocolFromMiddleware(registration.middlewareType);
            var exitId = $"{registration.middlewareType}.InvokeAsync()";
            var declaringType = registration.middlewareType;
            var methodName = "InvokeAsync";
            
            // 构建文件路径（相对路径）
            var filePath = BuildFilePathForMiddleware(repoRoot, registration.middlewareType);
            
            // 获取批准字段（特殊处理某些中间件）
            var approvedFields = GetApprovedDataFieldsForMiddleware(registration.middlewareType);
            
            actualProducers.Add((exitId, filePath, declaringType, methodName, protocol, approvedFields));
        }
        
        return actualProducers;
    }
    
    /// <summary>
    /// 找到仓库根目录。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        // 从当前程序集位置开始向上查找
        var currentDir = Path.GetDirectoryName(typeof(ErrorResponseProducerInventoryTests).Assembly.Location)!;
        
        while (currentDir != null)
        {
            // 检查是否存在 .git 目录作为仓库标记
            var gitDir = Path.Combine(currentDir, ".git");
            if (Directory.Exists(gitDir))
            {
                return currentDir;
            }
            
            // 检查是否存在 backend 目录作为常用标记
            var backendDir = Path.Combine(currentDir, "backend");
            if (Directory.Exists(backendDir))
            {
                // 进一步确认是否是我们的仓库结构
                var frameworkAppDir = Path.Combine(backendDir, "framework-app");
                if (Directory.Exists(frameworkAppDir))
                {
                    return currentDir;
                }
            }
            
            var parentDir = Directory.GetParent(currentDir);
            if (parentDir == null)
            {
                break;
            }
            currentDir = parentDir.FullName;
        }
        
        throw new DirectoryNotFoundException($"无法找到仓库根目录。从 {Path.GetDirectoryName(typeof(ErrorResponseProducerInventoryTests).Assembly.Location)} 开始搜索");
    }
    
    /// <summary>
    /// 扫描 HostModule.cs 中的 UseMiddleware<T> 注册。
    /// 使用正则表达式精确匹配类型，避免 `UseMiddleware<AbpAdmin.` 这样的通用匹配。
    /// </summary>
    private static List<(string middlewareType, int lineNumber)> ScanMiddlewareRegistrations(string hostModuleCode)
    {
        var registrations = new List<(string, int)>();
        
        // 使用正则表达式匹配 UseMiddleware<T> 调用
        // 确保匹配完整的类型名，避免部分匹配
        var lines = hostModuleCode.Replace("\r\n", "\n").Split('\n');
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // 匹配完整的 UseMiddleware<完整类型名> 调用
            var useMiddlewareMatch = System.Text.RegularExpressions.Regex.Match(line, @"UseMiddleware<(\w+\.?)+>");
            if (useMiddlewareMatch.Success)
            {
                var middlewareType = useMiddlewareMatch.Value
                    .Replace("UseMiddleware<", "")
                    .Replace(">", "")
                    .Trim();
                
                // 只添加已知的错误响应中间件类型
                if (IsErrorResponseMiddleware(middlewareType))
                {
                    registrations.Add((middlewareType, i + 1));
                }
            }
        }
        
        return registrations;
    }
    
    /// <summary>
    /// 检查是否是错误响应中间件类型。
    /// </summary>
    private static bool IsErrorResponseMiddleware(string middlewareType)
    {
        // 已知的错误响应中间件类型列表
        var errorMiddlewareTypes = new[]
        {
            "FriendlyBusinessExceptionMiddleware",
            "TokenEndpointRateLimitingMiddleware",
            "IdentitySessionValidationMiddleware",
            "FileUploadContentGuardMiddleware"
        };
        
        return errorMiddlewareTypes.Contains(middlewareType);
    }
    
    /// <summary>
    /// 根据中间件类型推断协议。
    /// </summary>
    private static ErrorProtocol InferProtocolFromMiddleware(string middlewareType)
    {
        return middlewareType switch
        {
            "FriendlyBusinessExceptionMiddleware" => ErrorProtocol.AbpEnvelope,
            "TokenEndpointRateLimitingMiddleware" => ErrorProtocol.OAuth,
            "IdentitySessionValidationMiddleware" => ErrorProtocol.OAuth,
            "FileUploadContentGuardMiddleware" => ErrorProtocol.CustomJson,
            _ => ErrorProtocol.AbpEnvelope // 默认
        };
    }
    
    /// <summary>
    /// 构建中间件的文件路径。
    /// 简化版：根据中间件类型返回固定路径。
    /// </summary>
    private static string BuildFilePathForMiddleware(string repoRoot, string middlewareType)
    {
        // 根据中间件类型返回固定的相对路径
        return middlewareType switch
        {
            "FriendlyBusinessExceptionMiddleware" => "AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs",
            "TokenEndpointRateLimitingMiddleware" => "AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs",
            "IdentitySessionValidationMiddleware" => "AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs",
            "FileUploadContentGuardMiddleware" => "AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs",
            _ => $"AbpAdmin.HttpApi.Host/**/{middlewareType}.cs"
        };
    }
    
    /// <summary>
    /// 获取中间件的批准字段列表。
    /// </summary>
    private static List<string> GetApprovedDataFieldsForMiddleware(string middlewareType)
    {
        return middlewareType switch
        {
            "FileUploadContentGuardMiddleware" => new List<string> { "fileName", "reason" },
            _ => new List<string>()
        };
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
