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
    /// 通过读取 HostModule.cs 和 HttpApiModule.cs 源码，解析所有真实注册类型。
    /// </summary>
    private static List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)> ScanProductionErrorProducers()
    {
        var actualProducers = new List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)>();
        
        var repoRoot = FindRepositoryRoot();
        
        // 读取 HostModule.cs 源码
        var hostModulePath = Path.Combine(repoRoot, "backend", "framework-app", "AbpAdmin.HttpApi.Host", "AbpAdminHttpApiHostModule.cs");
        if (!File.Exists(hostModulePath))
        {
            throw new FileNotFoundException($"无法找到 HostModule.cs: {hostModulePath}");
        }
        
        var hostModuleCode = File.ReadAllText(hostModulePath);
        
        // 1. 扫描扩展方法注册：UseFriendlyBusinessException, UseFileUploadContentGuard
        var extensionMethodRegistrations = ScanExtensionMethodRegistrations(hostModuleCode);
        actualProducers.AddRange(extensionMethodRegistrations);
        
        // 2. 扫描 UseMiddleware<T> 调用（支持短名和完整名，排除注释）
        var middlewareRegistrations = ScanUseMiddlewareRegistrations(hostModuleCode);
        actualProducers.AddRange(middlewareRegistrations);
        
        // 3. 扫描 Cookie 事件注册
        var cookieEventRegistrations = ScanCookieEventRegistrations(hostModuleCode);
        actualProducers.AddRange(cookieEventRegistrations);
        
        // 4. 扫描 TenantMismatch（UseMultiTenancy 启用时存在）
        if (hostModuleCode.Contains("UseMultiTenancy"))
        {
            actualProducers.Add((
                "AbpAdminHttpApiHostModule.TenantMismatch",
                "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
                "AbpAdminHttpApiHostModule",
                "OnTenantValidationFailure",
                ErrorProtocol.OAuth,
                new List<string>()
            ));
        }
        
        // 5. 扫描 ImageProcessingBusyExceptionFilter（MVC Filter）
        // 首先检查 HostModule，如果没有则检查 HttpApiModule
        var imageFilterAdded = hostModuleCode.Contains("ImageProcessingBusyExceptionFilter") ||
                              hostModuleCode.Contains("AddFilter<ImageProcessingBusyExceptionFilter>");
                              
        if (!imageFilterAdded)
        {
            // 在 HttpApiModule 中搜索
            var httpApiModulePath = Path.Combine(repoRoot, "backend", "framework-app", "AbpAdmin.HttpApi", "AbpAdminHttpApiModule.cs");
            if (File.Exists(httpApiModulePath))
            {
                var httpApiModuleCode = File.ReadAllText(httpApiModulePath);
                imageFilterAdded = httpApiModuleCode.Contains("ImageProcessingBusyExceptionFilter") ||
                                  httpApiModuleCode.Contains("AddFilter<ImageProcessingBusyExceptionFilter>");
            }
        }
        
        if (imageFilterAdded)
        {
            actualProducers.Add((
                "ImageProcessingBusyExceptionFilter.OnException()",
                "AbpAdmin.HttpApi/Imaging/ImageProcessingBusyExceptionFilter.cs",
                "ImageProcessingBusyExceptionFilter",
                "OnException",
                ErrorProtocol.AbpEnvelope,
                new List<string>()
            ));
        }
        
        return actualProducers;
    }
    
    /// <summary>
    /// 扫描扩展方法注册（UseFriendlyBusinessException, UseFileUploadContentGuard）。
    /// </summary>
    private static List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)> ScanExtensionMethodRegistrations(string hostModuleCode)
    {
        var result = new List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)>();
        
        // UseFriendlyBusinessException 扩展方法 -> FriendlyBusinessExceptionMiddleware
        if (hostModuleCode.Contains("UseFriendlyBusinessException()") || hostModuleCode.Contains("app.UseFriendlyBusinessException"))
        {
            result.Add((
                "FriendlyBusinessExceptionMiddleware.InvokeAsync()",
                "AbpAdmin.HttpApi.Host/FriendlyBusinessExceptionMiddleware.cs",
                "FriendlyBusinessExceptionMiddleware",
                "InvokeAsync",
                ErrorProtocol.AbpEnvelope,
                new List<string>()
            ));
        }
        
        // UseFileUploadContentGuard 扩展方法 -> FileUploadContentGuardMiddleware
        if (hostModuleCode.Contains("UseFileUploadContentGuard()") || hostModuleCode.Contains("app.UseFileUploadContentGuard"))
        {
            result.Add((
                "FileUploadContentGuardMiddleware.InvokeAsync()",
                "AbpAdmin.HttpApi.Host/Files/FileUploadContentGuardMiddleware.cs",
                "FileUploadContentGuardMiddleware",
                "InvokeAsync",
                ErrorProtocol.CustomJson,
                new List<string> { "fileName", "reason" }
            ));
        }
        
        return result;
    }
    
    /// <summary>
    /// 扫描 UseMiddleware<T> 注册。支持短名和完整命名空间类型名，排除注释行。
    /// </summary>
    private static List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)> ScanUseMiddlewareRegistrations(string hostModuleCode)
    {
        var result = new List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)>();
        
        // 已知错误响应中间件类型映射（短名到完整信息）
        var knownMiddlewareTypes = new Dictionary<string, (string exitId, string filePath, ErrorProtocol protocol, List<string> approvedFields)>(StringComparer.OrdinalIgnoreCase)
        {
            ["TokenEndpointRateLimitingMiddleware"] = ("TokenEndpointRateLimitingMiddleware.InvokeAsync()", "AbpAdmin.HttpApi.Host/OpenIddict/TokenEndpointRateLimitingMiddleware.cs", ErrorProtocol.OAuth, new List<string>()),
            ["IdentitySessionValidationMiddleware"] = ("IdentitySessionValidationMiddleware.InvokeAsync()", "AbpAdmin.HttpApi.Host/Identity/IdentitySessionValidationMiddleware.cs", ErrorProtocol.OAuth, new List<string>()),
        };
        
        // 按行处理，排除注释
        var lines = hostModuleCode.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // 跳过注释行
            if (line.TrimStart().StartsWith("//"))
            {
                continue;
            }
            
            // 查找 UseMiddleware<T> 调用
            var useMiddlewareIndex = line.IndexOf("UseMiddleware<");
            if (useMiddlewareIndex >= 0)
            {
                // 提取完整类型名
                var start = useMiddlewareIndex + "UseMiddleware<".Length;
                var end = line.IndexOf('>', start);
                if (end > start)
                {
                    var fullTypeName = line.Substring(start, end - start).Trim();
                    
                    // 提取短名（最后一段）
                    var shortName = fullTypeName;
                    if (fullTypeName.Contains('.'))
                    {
                        shortName = fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1);
                    }
                    
                    // 检查是否是已知的错误响应中间件
                    if (knownMiddlewareTypes.TryGetValue(shortName, out var info))
                    {
                        result.Add((
                            info.exitId,
                            info.filePath,
                            shortName,
                            "InvokeAsync",
                            info.protocol,
                            info.approvedFields
                        ));
                    }
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 扫描 Cookie 事件注册（OnRedirectToLogin, OnRedirectToAccessDenied）。
    /// </summary>
    private static List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)> ScanCookieEventRegistrations(string hostModuleCode)
    {
        var result = new List<(string ExitId, string FilePath, string DeclaringType, string MethodName, ErrorProtocol Protocol, List<string> ApprovedDataFields)>();
        
        // 扫描 OnRedirectToLogin 事件注册
        if (hostModuleCode.Contains("OnRedirectToLogin"))
        {
            result.Add((
                "CookieAuthenticationEvents.OnRedirectToLogin",
                "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
                "CookieAuthenticationEvents",
                "OnRedirectToLogin",
                ErrorProtocol.AbpEnvelope,
                new List<string>()
            ));
        }
        
        // 扫描 OnRedirectToAccessDenied 事件注册
        if (hostModuleCode.Contains("OnRedirectToAccessDenied"))
        {
            result.Add((
                "CookieAuthenticationEvents.OnRedirectToAccessDenied",
                "AbpAdmin.HttpApi.Host/AbpAdminHttpApiHostModule.cs",
                "CookieAuthenticationEvents",
                "OnRedirectToAccessDenied",
                ErrorProtocol.AbpEnvelope,
                new List<string>()
            ));
        }
        
        return result;
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
    /// 根据中间件类型推断协议。
    /// </summary>
    private static ErrorProtocol InferProtocolFromMiddleware(string middlewareType)
    {
        // 提取短名（如果包含完整命名空间）
        var shortName = middlewareType;
        if (middlewareType.Contains('.'))
        {
            shortName = middlewareType.Substring(middlewareType.LastIndexOf('.') + 1);
        }
        
        return shortName switch
        {
            "FriendlyBusinessExceptionMiddleware" => ErrorProtocol.AbpEnvelope,
            "TokenEndpointRateLimitingMiddleware" => ErrorProtocol.OAuth,
            "IdentitySessionValidationMiddleware" => ErrorProtocol.OAuth,
            "FileUploadContentGuardMiddleware" => ErrorProtocol.CustomJson,
            _ => ErrorProtocol.AbpEnvelope // 默认
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
