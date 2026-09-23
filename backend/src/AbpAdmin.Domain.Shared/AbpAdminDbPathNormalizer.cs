using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin;

/// <summary>
/// SQLite 相对路径连接串的 cwd 归一（仅开发/仓库布局生效）。
/// <para>
/// appsettings 里 <c>Data Source=../../AbpAdmin.db</c> 按【进程 cwd】解析：
/// 从项目目录启动指向仓库根（预期），从仓库根 <c>dotnet run</c> 则静默在仓库外
/// 新建一个空库（迁移+种子全写进错误数据库，与 Host 形成脑裂且零报错）。
/// 本助手从程序集位置向上找 <c>AbpAdmin.slnx</c> 锚定仓库根，把该相对形态整体改写为
/// 基于锚的绝对路径，使行为与启动 cwd 无关；发布布局找不到锚时保持原样不动。
/// </para>
/// </summary>
public static class AbpAdminDbPathNormalizer
{
    private const string RepoAnchorFileName = "AbpAdmin.slnx";
    private const string ConnectionStringKey = "ConnectionStrings:Default";
    private const string RelativeDataSource = "Data Source=../../AbpAdmin.db";

    /// <summary>
    /// 命中并改写时返回应用后的绝对库路径；未命中（非该相对形态 / 找不到仓库锚）返回 false。
    /// 必须在 ABP 读取 AbpDbConnectionOptions（模块初始化）之前调用。
    /// </summary>
    public static bool TryNormalize(IConfiguration configuration, out string? appliedPath)
    {
        appliedPath = null;
        var connectionString = configuration[ConnectionStringKey];
        if (connectionString == null ||
            !connectionString.Contains(RelativeDataSource, StringComparison.Ordinal))
        {
            return false;
        }

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot == null)
        {
            return false;
        }

        var dbPath = Path.Combine(repoRoot, "AbpAdmin.db").Replace('\\', '/');
        // 整段替换相对形态，连接串的其余部分（参数等）原样保留
        configuration[ConnectionStringKey] = connectionString.Replace(
            RelativeDataSource,
            $"Data Source={dbPath}",
            StringComparison.Ordinal);
        appliedPath = dbPath;
        return true;
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoAnchorFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
