using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 存量库自举打戳：脚本时代（迁移机制引入前）建的库「业务表在而 History 缺失」，
/// MigrateAsync 会重放 Initial 撞 42P07「relation already exists」→ 宿主起不来。
/// 与 nopCommerce(MigrationVersionInfo)/Umbraco(umbracoMigration) 及微软「存量库接入
/// 迁移须记账不重放」指引同模式。
/// 哨兵必须用所属 DbContext 的自有表，不能用跨模块的表（如框架的 AbpUsers）——全新库上
/// 框架迁移器先跑会先建出 AbpUsers，后跑模块若以其为哨兵会把自己的 Initial 也戳掉，
/// 导致自己的表永远建不出来（BizTemplate 迁移器注释有详述）。
/// 表名一律不加 schema 限定（与 EF 记账同走 search_path）：连接串带 SearchPath 隔离
/// 部署时，探针看到的是 EF 将要用的那本账，不会往 public 打无人读的假账。
/// PostgreSQL 专用（to_regclass 哨兵探测；非 Npgsql 连接直接 NotSupportedException——
/// 提供程序决定权只在这一处，调用方不要提前强转）。三态决策抽为纯函数
/// <see cref="Decide"/>，决策表由单元测试直接覆盖（含「全新库不误戳」陷阱）。
/// </summary>
public static class EfCoreLegacySchemaBaseliner
{
    public const string FrameworkHistoryTable = "__EFMigrationsHistory";
    public const string FrameworkSentinelTable = "AbpUsers";
    /// <summary>与本工程 Migrations/ 下首个迁移一致；漂移由 EfCoreLegacySchemaBaselinerDecideTests 工程测试钉死。</summary>
    public const string FrameworkInitialMigrationId = "20261007125402_Initial";

    public static async Task StampIfNeededAsync(
        DbConnection connection,
        ILogger logger,
        string historyTableName,
        string constraintName,
        string initialMigrationId,
        string baselineProductVersion,
        string sentinelTableName)
    {
        if (connection is not NpgsqlConnection npgsqlConnection)
        {
            throw new NotSupportedException(
                $"存量库哨兵探测不支持提供程序 {connection.GetType().Name}（仅 Npgsql——" +
                "本工程迁移已收敛 PostgreSQL 单提供程序）。");
        }

        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync();
        }

        try
        {
            var initialRecorded = false;
            if (await TableExistsAsync(npgsqlConnection, historyTableName))
            {
                await using var check = new NpgsqlCommand(
                    $"SELECT 1 FROM \"{historyTableName}\" WHERE \"MigrationId\" = $1 LIMIT 1",
                    npgsqlConnection);
                check.Parameters.AddWithValue(initialMigrationId);
                initialRecorded = await check.ExecuteScalarAsync() is not null;
            }

            var sentinelExists = await TableExistsAsync(npgsqlConnection, sentinelTableName);
            if (Decide(initialRecorded, sentinelExists) != BaselineDecision.StampBaseline)
            {
                return;
            }

            await CreateHistoryTableIfAbsentAsync(npgsqlConnection, historyTableName, constraintName);

            await using var stamp = new NpgsqlCommand(
                "INSERT INTO \"«H»\" (\"MigrationId\", \"ProductVersion\") " +
                "VALUES ($1, $2) ON CONFLICT (\"MigrationId\") DO NOTHING;"
                    .Replace("«H»", historyTableName),
                npgsqlConnection);
            stamp.Parameters.AddWithValue(initialMigrationId);
            stamp.Parameters.AddWithValue(baselineProductVersion);
            await stamp.ExecuteNonQueryAsync();

            // Warning 而非 Information：打戳隐含「存量 schema ≈ Initial」的信任假设，
            // 运维应知晓并（一次性）比对 schema-only dump 与 migrations script 确认无漂移
            logger.LogWarning(
                "检测到存量库（{Sentinel} 表存在但 {History} 无 {Initial} 记账，schema 建于迁移机制引入前），" +
                "已自动打戳 baseline：{Initial}@{Version}。后续迁移将正常增量应用。" +
                "建议运维一次性执行 pg_dump --schema-only 与 dotnet ef migrations script 比对确认无漂移。",
                sentinelTableName, historyTableName, initialMigrationId,
                initialMigrationId, baselineProductVersion);
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// 三态决策（纯函数，单元测试覆盖）：
    /// History 已记 Initial → <see cref="BaselineDecision.SkipAlreadyRecorded"/>（正常路径，
    ///   幂等重入由此收敛——打戳后的每一次启动都走这里）；
    /// 未记账且哨兵表不存在 → <see cref="BaselineDecision.FreshDatabase"/>（全新空库，
    ///   交由 MigrateAsync 正常建全部；「全新库不误戳」陷阱场景在此挡下）；
    /// 未记账且哨兵表存在 → <see cref="BaselineDecision.StampBaseline"/>（脚本时代存量库）。
    /// </summary>
    public static BaselineDecision Decide(bool initialRecorded, bool sentinelExists)
    {
        if (initialRecorded)
        {
            return BaselineDecision.SkipAlreadyRecorded;
        }

        return sentinelExists ? BaselineDecision.StampBaseline : BaselineDecision.FreshDatabase;
    }

    /// <summary>
    /// 建记账表（幂等）。CREATE TABLE IF NOT EXISTS 在并发首启时有已知竞态
    /// （两连接同时通过存在性检查→一方 42P07），容忍之——另一方必然已建表成功，
    /// 后续 INSERT ON CONFLICT 保证记账幂等。
    /// </summary>
    private static async Task CreateHistoryTableIfAbsentAsync(
        NpgsqlConnection connection, string historyTableName, string constraintName)
    {
        try
        {
            await using var createHistory = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS \"«H»\" (" +
                "\"MigrationId\" character varying(150) NOT NULL, " +
                "\"ProductVersion\" character varying(32) NOT NULL, " +
                "CONSTRAINT \"«PK»\" PRIMARY KEY (\"MigrationId\"));"
                    .Replace("«H»", historyTableName)
                    .Replace("«PK»", constraintName),
                connection);
            await createHistory.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P07")
        {
            // 并发首启对手已建表——视为成功
        }
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName)
    {
        await using var command = new NpgsqlCommand(
            // 不带 schema 限定：随 search_path 解析——看到的是 EF 记账/建表会用的那一份
            "SELECT to_regclass($1) IS NOT NULL",
            connection);
        command.Parameters.AddWithValue($"\"{tableName}\"");
        return await command.ExecuteScalarAsync() is true;
    }
}

/// <summary>存量库打戳决策。</summary>
public enum BaselineDecision
{
    /// <summary>History 已记 Initial——正常路径，直接跳过。</summary>
    SkipAlreadyRecorded,
    /// <summary>哨兵表不存在——全新空库，不打戳，交由迁移正常建全部。</summary>
    FreshDatabase,
    /// <summary>表在而未记账——脚本时代存量库，打戳。</summary>
    StampBaseline,
}
