using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 按脚本 Id（文件名去 .sql，Ordinal 序）执行程序集内 <c>Sql/{provider}</c> 嵌入脚本，并用 History 表记账。
/// 框架库 History 表沿用 <c>__EFMigrationsHistory</c>：Id 与历史记账一致的脚本精确跳过，
/// 记账缺失（更早迁移代次、改名、手工建库）则基线重放——基线全量 IF NOT EXISTS 幂等，
/// 已存在对象原样保留，可安全收敛，不存在"重放撞表"死角。
/// 脚本编写契约：
/// 1) 资源经 csproj 的 <c>EmbeddedResource Include="Sql\**\*.sql"</c> 嵌入，目录必须与 providerFolder 同名（postgresql/sqlite）；
/// 2) 追加脚本用零填充编号（002_、010_……），Ordinal 序下 100_ 会排在 20_ 之前；
/// 3) 已应用的脚本不可改名/删除——Id 是唯一的记账键；
/// 4) 语句按分号切分（引号/注释内的分号不切，见 <see cref="SplitStatements"/>），单条语句不要内嵌事务控制。
/// 幂等语义：每个脚本的语句与其记账行在同一事务内提交——任一语句失败整体回滚为「未应用」，重跑从零执行该脚本。
/// 注意 <see cref="HasPendingAsync"/> 只读不建表：History 表不存在会抛错，宿主/调用方视为「需要迁移」。
/// </summary>
public static class EmbeddedSqlScriptMigrator
{
    public const string FrameworkHistoryTable = "__EFMigrationsHistory";

    private const string ProductVersion = "sql";

    public static async Task ApplyAsync(
        DbContext context,
        string providerFolder,
        string historyTable,
        Assembly assembly,
        ILogger logger,
        string logName)
    {
        var scripts = LoadScripts(assembly, providerFolder);
        logger.LogInformation(
            "{LogName}脚本开始（{Provider}，History: {History}，{Count} 个文件）...",
            logName,
            providerFolder,
            historyTable,
            scripts.Count);

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            // 宿主显式事务极少存在（迁移在 UoW 之外跑）；存在则并入，由调用方提交
            var ambient = context.Database.CurrentTransaction?.GetDbTransaction();

            await ExecuteAsync(connection, ambient, HistoryDdl(providerFolder, historyTable));
            var applied = await ReadAppliedAsync(connection, ambient, historyTable);

            foreach (var script in scripts)
            {
                if (applied.Contains(script.Id))
                {
                    continue;
                }

                logger.LogInformation("应用 {LogName}脚本 {ScriptId}", logName, script.Id);
                if (ambient != null)
                {
                    await ApplyScriptAsync(connection, ambient, historyTable, script);
                }
                else
                {
                    // 无环境事务：一个脚本 = 一个事务，失败回滚，库里不会留下半套表 + 无记账的死角
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    await ApplyScriptAsync(connection, transaction.GetDbTransaction(), historyTable, script);
                    await transaction.CommitAsync();
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        logger.LogInformation("{LogName}脚本完成。", logName);
    }

    public static async Task<bool> HasPendingAsync(
        DbContext context,
        string providerFolder,
        string historyTable,
        Assembly assembly)
    {
        var scripts = LoadScripts(assembly, providerFolder);
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        try
        {
            var applied = await ReadAppliedAsync(
                connection, context.Database.CurrentTransaction?.GetDbTransaction(), historyTable);
            return scripts.Any(script => !applied.Contains(script.Id));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    // 语句与记账行同事务：任一语句失败整体回滚为「未应用」，重跑从零执行该脚本而非撞上已建对象
    private static async Task ApplyScriptAsync(
        DbConnection connection,
        DbTransaction transaction,
        string historyTable,
        SqlScript script)
    {
        foreach (var statement in SplitStatements(script.Sql))
        {
            await ExecuteAsync(connection, transaction, statement);
        }

        await InsertHistoryAsync(connection, transaction, historyTable, script.Id);
    }

    private static List<SqlScript> LoadScripts(Assembly assembly, string providerFolder)
    {
        var marker = $".Sql.{providerFolder}.";
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(marker, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name => new SqlScript(ScriptId(name, providerFolder), ReadResource(assembly, name)))
            .OrderBy(script => script.Id, StringComparer.Ordinal)
            .ToList();

        if (scripts.Count == 0)
        {
            throw new InvalidOperationException($"未在程序集 {assembly.GetName().Name} 中找到 Sql/{providerFolder} 下的建表脚本。");
        }

        return scripts;
    }

    // 用完整 marker 解析：marker 之后就是「文件名.sql」，不再依赖文件夹内无点
    private static string ScriptId(string resourceName, string providerFolder)
    {
        var marker = $".Sql.{providerFolder}.";
        var file = resourceName[(resourceName.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        return file.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"无法读取嵌入脚本 {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string HistoryDdl(string providerFolder, string historyTable)
    {
        if (providerFolder == AbpAdminDatabaseProvider.ScriptFolderPostgreSql)
        {
            return $"""
                CREATE TABLE IF NOT EXISTS "{historyTable}" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK_{historyTable}" PRIMARY KEY ("MigrationId")
                )
                """;
        }

        return $"""
            CREATE TABLE IF NOT EXISTS "{historyTable}" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK_{historyTable}" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            )
            """;
    }

    private static async Task<HashSet<string>> ReadAppliedAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string historyTable)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""SELECT "MigrationId" FROM "{historyTable}" """;

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static async Task InsertHistoryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string historyTable,
        string scriptId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO "{historyTable}" ("MigrationId", "ProductVersion") VALUES (@id, @ver)
            """;

        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = scriptId;
        command.Parameters.Add(id);

        var version = command.CreateParameter();
        version.ParameterName = "@ver";
        version.Value = ProductVersion;
        command.Parameters.Add(version);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 引号感知的语句切分：仅切分「引号与注释之外」的分号，并剥除注释。
    /// 支持：-- 行注释、块注释、'字符串'（'' 转义）、"标识符"（"" 转义）、$tag$ 美元引号（PG 函数体）。
    /// $ 后非合法标识符（如 $1 位置参数）不视为美元引号开头，按普通字符处理。
    /// </summary>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var statement = new StringBuilder(sql.Length);
        var index = 0;

        while (index < sql.Length)
        {
            var ch = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            // -- 行注释：吞到行尾（不含换行，保留语句内换行结构）
            if (ch == '-' && next == '-')
            {
                var end = sql.IndexOf('\n', index);
                index = end < 0 ? sql.Length : end;
                continue;
            }

            // 块注释：整体剥除，替换为一个空格防止相邻 token 粘连
            if (ch == '/' && next == '*')
            {
                var end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? sql.Length : end + 2;
                statement.Append(' ');
                continue;
            }

            // '...' 字符串（'' 转义）与 "..." 引用标识符（"" 转义）：引号内一切原样保留
            if (ch == '\'' || ch == '"')
            {
                statement.Append(ch);
                index++;
                while (index < sql.Length)
                {
                    statement.Append(sql[index]);
                    if (sql[index] == ch)
                    {
                        if (index + 1 < sql.Length && sql[index + 1] == ch)
                        {
                            statement.Append(sql[index + 1]);
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    index++;
                }

                continue;
            }

            // $tag$ 美元引号（PostgreSQL 函数体）：tag 为空或字母/下划线开头的标识符才成立
            if (ch == '$')
            {
                var closing = sql.IndexOf('$', index + 1);
                if (IsDollarQuoteTag(sql, index, closing))
                {
                    var tagLength = closing - index + 1;
                    var contentEnd = sql.IndexOf(sql.AsSpan(index, tagLength).ToString(), closing + 1, StringComparison.Ordinal);
                    contentEnd = contentEnd < 0 ? sql.Length : contentEnd + tagLength;
                    statement.Append(sql.AsSpan(index, contentEnd - index));
                    index = contentEnd;
                    continue;
                }
            }

            if (ch == ';')
            {
                var trimmed = statement.ToString().Trim();
                statement.Clear();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }

                index++;
                continue;
            }

            statement.Append(ch);
            index++;
        }

        var tail = statement.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    // $…$ 中间为空或 [A-Za-z_][A-Za-z0-9_]* 才是美元引号定界符；$1 这类位置参数不是
    private static bool IsDollarQuoteTag(string sql, int opening, int closing)
    {
        if (closing < 0)
        {
            return false;
        }

        var innerLength = closing - opening - 1;
        if (innerLength == 0)
        {
            return true;
        }

        var first = sql[opening + 1];
        if (!char.IsAsciiLetter(first) && first != '_')
        {
            return false;
        }

        for (var i = opening + 2; i < closing; i++)
        {
            var c = sql[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private sealed record SqlScript(string Id, string Sql);
}
