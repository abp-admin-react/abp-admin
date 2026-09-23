using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// EmbeddedSqlScriptMigrator 端到端契约（真实 Sqlite 基线脚本，不 mock）：
/// 空库首跑建全表并记账；重跑幂等零执行；History 缺表按约定抛错（调用方视为"需要迁移"）。
/// 旧库升级路径依赖脚本 Id 与被替换 EF 迁移 Id 完全一致——一并钉住，防止基线文件被改名。
/// </summary>
public class EmbeddedSqlScriptMigratorTests
{
    private const string BaselineScriptId = "20260922005124_Initial";

    [Fact]
    public async Task ApplyAsync_Should_Create_Tables_And_Record_History_On_Empty_Database()
    {
        using var database = new AbpUnitTestSqliteDatabase();
        await using var context = CreateContext(database.ConnectionString);

        await EmbeddedSqlScriptMigrator.ApplyAsync(
            context, "sqlite", EmbeddedSqlScriptMigrator.FrameworkHistoryTable,
            typeof(AbpAdminDbContext).Assembly, NullLogger.Instance, "测试库");

        var historyCount = await CountHistoryRowsAsync(context);
        Assert.Equal(1, historyCount);

        // 基线脚本 Id 必须与被替换的 EF 迁移 Id 一致（旧 __EFMigrationsHistory 记账的库据此跳过基线）
        var migrationId = await QuerySingleAsync(
            context, $"""SELECT "MigrationId" FROM "{EmbeddedSqlScriptMigrator.FrameworkHistoryTable}" """);
        Assert.Equal(BaselineScriptId, migrationId);

        // 抽查基线表结构真实建出来了（数据行由迁移服务的种子步骤负责，不在脚本迁移器范围）
        Assert.Equal(1L, await QuerySingleAsync(
            context, """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AbpUsers' """));
    }

    [Fact]
    public async Task ApplyAsync_Should_Be_Idempotent_On_Second_Run()
    {
        using var database = new AbpUnitTestSqliteDatabase();
        await using var context = CreateContext(database.ConnectionString);
        var historyTable = EmbeddedSqlScriptMigrator.FrameworkHistoryTable;

        await EmbeddedSqlScriptMigrator.ApplyAsync(
            context, "sqlite", historyTable, typeof(AbpAdminDbContext).Assembly, NullLogger.Instance, "测试库");
        await EmbeddedSqlScriptMigrator.ApplyAsync(
            context, "sqlite", historyTable, typeof(AbpAdminDbContext).Assembly, NullLogger.Instance, "测试库");

        Assert.Equal(1, await CountHistoryRowsAsync(context));

        var pending = await EmbeddedSqlScriptMigrator.HasPendingAsync(
            context, "sqlite", historyTable, typeof(AbpAdminDbContext).Assembly);
        Assert.False(pending);
    }

    [Fact]
    public async Task HasPendingAsync_Should_Throw_When_History_Table_Missing()
    {
        using var database = new AbpUnitTestSqliteDatabase();
        await using var context = CreateContext(database.ConnectionString);

        // 空库没有 History 表：HasPending 只读不建表，抛错由调用方（宿主启动检查）兜底为"需要迁移"
        await Assert.ThrowsAnyAsync<Exception>(() =>
            EmbeddedSqlScriptMigrator.HasPendingAsync(
                context, "sqlite", EmbeddedSqlScriptMigrator.FrameworkHistoryTable,
                typeof(AbpAdminDbContext).Assembly));
    }

    private static AbpAdminDbContext CreateContext(string connectionString)
    {
        return new AbpAdminDbContext(new DbContextOptionsBuilder<AbpAdminDbContext>()
            .UseSqlite(connectionString)
            .Options);
    }

    private static async Task<int> CountHistoryRowsAsync(AbpAdminDbContext context)
    {
        var value = await QuerySingleAsync(
            context, $"""SELECT COUNT(*) FROM "{EmbeddedSqlScriptMigrator.FrameworkHistoryTable}" """);
        return int.Parse(value!.ToString()!);
    }

    private static async Task<object?> QuerySingleAsync(AbpAdminDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
