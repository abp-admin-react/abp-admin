using System;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.EntityFrameworkCore;

public static class AbpAdminDatabaseProvider
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSql";

    // Sql/ 脚本目录名（与 csproj 的 Sql\<目录>\*.sql 嵌入约定一致），是 provider 的唯一第二表示
    public const string ScriptFolderPostgreSql = "postgresql";
    public const string ScriptFolderSqlite = "sqlite";

    public static string Get(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        return string.IsNullOrWhiteSpace(provider) ? Sqlite : provider.Trim();
    }

    public static bool IsPostgreSql(IConfiguration configuration)
    {
        return Get(configuration).Equals(PostgreSql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按 Database:Provider 解析嵌入式建表脚本目录（EmbeddedSqlScriptMigrator 的 folder 参数）。</summary>
    public static string GetScriptFolder(IConfiguration configuration)
    {
        return IsPostgreSql(configuration) ? ScriptFolderPostgreSql : ScriptFolderSqlite;
    }
}
