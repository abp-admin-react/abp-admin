using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// EmbeddedSqlScriptMigrator 端到端契约（真实 Sqlite 基线脚本，不 mock）：
/// 空库首跑建全表并记账；重跑幂等零执行；History 缺表按约定抛错（调用方视为"需要迁移"）；
/// 记账缺失时基线重放收敛；脚本中途失败整体回滚为「未应用」；多脚本按 Ordinal 序执行。
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

        // 空库没有 History 表：HasPending 只读不建表，抛错由调用方（宿主启动检查）兜底为"需要迁移"。
        // 契约是 SqliteException（no such table），收窄类型防止接线 NRE 之类的无关异常蒙混过关。
        // 先开一次连接让库文件落盘——否则"文件不存在"防呆守卫（InvalidOperationException）会先于
        // 本用例要验的"缺表 SqliteException"触发；两个异常都被调用方兜底为"需要迁移"，契约分开钉
        await context.Database.OpenConnectionAsync();
        await context.Database.CloseConnectionAsync();

        await Assert.ThrowsAsync<SqliteException>(() =>
            EmbeddedSqlScriptMigrator.HasPendingAsync(
                context, "sqlite", EmbeddedSqlScriptMigrator.FrameworkHistoryTable,
                typeof(AbpAdminDbContext).Assembly));
    }

    [Fact]
    public async Task ApplyAsync_Should_Rollback_Failed_Script_And_Keep_Script_Order()
    {
        using var database = new AbpUnitTestSqliteDatabase();
        await using var context = CreateContext(database.ConnectionString);
        var historyTable = "__OcrTestHistory";

        // 测试程序集自带三脚本：0010 建表 → 0020 建索引（依赖 0010，乱序必炸）→ 0030 建表后故意失败
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            EmbeddedSqlScriptMigrator.ApplyAsync(
                context, "sqlite", historyTable,
                typeof(EmbeddedSqlScriptMigratorTests).Assembly, NullLogger.Instance, "OCR测试库"));

        // 0030 的失败语句是 INSERT INTO 不存在的表
        Assert.Contains("no_such_table_ocr", exception.Message);

        // 顺序契约：0010/0020 已应用（0020 依赖 0010 的表，乱序会在 0020 先炸）
        Assert.Equal(1L, await QuerySingleAsync(context,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ord_probe' """));
        Assert.Equal(1L, await QuerySingleAsync(context,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_ord_probe' """));

        // 回滚契约：0030 的建表与记账整体回滚——失败不留半套对象、不留记账行
        Assert.Equal(0L, await QuerySingleAsync(context,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'rollback_probe' """));
        Assert.Equal(2, Convert.ToInt32(await QuerySingleAsync(
            context, $"""SELECT COUNT(*) FROM "{historyTable}" """)));

        // HasPending 正向分支：0030 仍 pending → true（三态契约的 true 态）
        Assert.True(await EmbeddedSqlScriptMigrator.HasPendingAsync(
            context, "sqlite", historyTable, typeof(EmbeddedSqlScriptMigratorTests).Assembly));

        // 重跑：已应用的两个精确跳过，0030 从零重试并再次整体回滚——状态收敛不变
        await Assert.ThrowsAsync<SqliteException>(() =>
            EmbeddedSqlScriptMigrator.ApplyAsync(
                context, "sqlite", historyTable,
                typeof(EmbeddedSqlScriptMigratorTests).Assembly, NullLogger.Instance, "OCR测试库"));
        Assert.Equal(2, Convert.ToInt32(await QuerySingleAsync(
            context, $"""SELECT COUNT(*) FROM "{historyTable}" """)));
        Assert.Equal(0L, await QuerySingleAsync(context,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'rollback_probe' """));
    }

    [Fact]
    public async Task ApplyAsync_Should_Replay_Baseline_When_History_Row_Missing()
    {
        using var database = new AbpUnitTestSqliteDatabase();
        await using var context = CreateContext(database.ConnectionString);
        var historyTable = EmbeddedSqlScriptMigrator.FrameworkHistoryTable;

        await EmbeddedSqlScriptMigrator.ApplyAsync(
            context, "sqlite", historyTable, typeof(AbpAdminDbContext).Assembly, NullLogger.Instance, "测试库");
        // 模拟"手工建库/更早迁移代次"：对象在、记账缺失
        await ExecuteAsync(context, $"""DELETE FROM "{historyTable}" """);

        // 基线重放收敛：全量 IF NOT EXISTS 幂等——已存在对象原样保留，缺失记账补齐
        await EmbeddedSqlScriptMigrator.ApplyAsync(
            context, "sqlite", historyTable, typeof(AbpAdminDbContext).Assembly, NullLogger.Instance, "测试库");

        Assert.Equal(1, await CountHistoryRowsAsync(context));
        Assert.Equal(1L, await QuerySingleAsync(context,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'AbpUsers' """));
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

    private static async Task ExecuteAsync(AbpAdminDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
