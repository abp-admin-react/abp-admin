using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace AbpAdmin;

/// <summary>
/// 真库（宿主 PostgreSQL）门控 Fact：发现期解析宿主连接串的"真实覆盖"——
/// 环境变量 ConnectionStrings__Default 优先，其后 src/AbpAdmin.HttpApi.Host/appsettings.secrets.json
/// （入库的开发样本，或被 skip-worktree 本地化的真实凭据）。tracked 基座 appsettings.json 的
/// PG 占位串（Password=CHANGE_ME，设计为运行期 fail-fast）不算配置。
/// 常规路径：使用本特性的测试程序集在加载时经 AbpAdminTestPg.TrySetHostOverrideEnvVar
/// 把 Testcontainers 容器连接串放进环境变量（本地与 CI 都要求 Docker），真库用例随进程
/// 容器真跑；容器不可用且连样本都没有时才 Skipped——机制与 Storage.Tests/TestInfrastructure/
/// RequiresEnvFactAttribute 同款，CI 报告可区分跳过与通过。
/// </summary>
public sealed class RequiresHostDatabaseFactAttribute : FactAttribute
{
    public const string OverrideEnvVar = "ConnectionStrings__Default";

    public RequiresHostDatabaseFactAttribute()
    {
        if (TryResolveOverride() == null)
        {
            Skip = "SKIPPED: 宿主连接串无真实覆盖（环境变量 ConnectionStrings__Default 或 " +
                   "src/AbpAdmin.HttpApi.Host/appsettings.secrets.json 均未提供 ConnectionStrings:Default；" +
                   "tracked 占位串不算配置）——真库用例，无凭证跳过";
        }
    }

    /// <summary>
    /// 解析宿主连接串的真实覆盖层，门控判定与测试体取串共用同一实现；无覆盖返回 null。
    /// 非 PG 形态串（Data Source= 等）视同未配置（宿主已 PG-only）。secrets 用与 AddJsonFile
    /// 相同的宽容参数解析（跳过注释、允许尾逗号——本地 secrets 惯用注释排版），坏 JSON 仍直接抛出，
    /// 与 AddJsonFile 行为一致：坏凭证宁可红，不可静默跳过。
    /// </summary>
    public static string? TryResolveOverride()
    {
        var envConn = Environment.GetEnvironmentVariable(OverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(envConn))
        {
            return Normalize(envConn);
        }

        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            return null;
        }

        var secretsPath = Path.Combine(repoRoot, "src", "AbpAdmin.HttpApi.Host", "appsettings.secrets.json");
        if (!File.Exists(secretsPath))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(
            File.ReadAllText(secretsPath),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var connections) ||
            connections.ValueKind != JsonValueKind.Object ||
            !connections.TryGetProperty("Default", out var def) ||
            def.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Normalize(def.GetString());
    }

    private static string? Normalize(string? conn) =>
        string.IsNullOrWhiteSpace(conn) || conn.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            ? null
            : conn;

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AbpAdmin.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }
}
