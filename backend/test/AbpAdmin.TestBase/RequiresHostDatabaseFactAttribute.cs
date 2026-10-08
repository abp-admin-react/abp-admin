using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace AbpAdmin;

/// <summary>
/// 真库（宿主 PostgreSQL）门控 Fact：发现期解析宿主连接串的"真实覆盖"——
/// 环境变量 ConnectionStrings__Default 优先，其后 src/AbpAdmin.HttpApi.Host/appsettings.secrets.json
/// （即宿主 Program.cs 同序分层中刨去 tracked 基座的部分）。
/// tracked 的 appsettings.json 基座是 PG 占位串（Password=CHANGE_ME，设计为运行期 fail-fast，
/// 见其"兜底连接串"注释），不算配置；无覆盖时用例标记为 Skipped——CI 干净 checkout 无凭证，
/// 真库用例按仓库既有约定（Storage.Tests/TestInfrastructure/RequiresEnvFactAttribute 同款机制）
/// 显示 Skipped 而非红。
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
    /// SQLite 形态串视同未配置（宿主已 PG-only）。secrets 用与 AddJsonFile 相同的宽容参数解析
    /// （跳过注释、允许尾逗号——本地 secrets 惯用注释排版），坏 JSON 仍直接抛出，
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
