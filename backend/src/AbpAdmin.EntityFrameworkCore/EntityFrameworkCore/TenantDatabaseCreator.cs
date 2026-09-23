using System;
using System.IO;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantDatabaseCreator"/> 的实现：按 Database:Provider（AbpAdminDatabaseProvider）
/// 建独立租户库，租户库与 host 同 DBMS（连接串管理界面的语义约定）。
/// - PostgreSql：连维护库 postgres 查 pg_database，缺失则 CREATE DATABASE；
/// - Sqlite：确保文件父目录存在，开/关一次连接创建空库文件（空文件也满足 EF 迁移前提）；
/// - 其余 provider 抛 NotSupportedException（与 AbpAdminDatabaseProvider 的支持面一致）。
/// 日志契约：新建库/文件记 Information（带库名/路径，不含凭据）；已存在走 ensure 常态分支
/// 记 Debug——上游 handler 的「Ensuring... / ensured and migrated」配这里的明细定位实际动作。
/// </summary>
public class TenantDatabaseCreator : ITenantDatabaseCreator, ITransientDependency
{
    public ILogger<TenantDatabaseCreator> Logger { get; set; }

    private readonly IConfiguration _configuration;

    public TenantDatabaseCreator(IConfiguration configuration)
    {
        _configuration = configuration;
        Logger = NullLogger<TenantDatabaseCreator>.Instance;
    }

    public async Task CreateIfNotExistsAsync(string connectionString)
    {
        if (AbpAdminDatabaseProvider.IsPostgreSql(_configuration))
        {
            await EnsurePostgreSqlDatabaseAsync(connectionString);
            return;
        }

        if (AbpAdminDatabaseProvider.Get(_configuration)
                .Equals(AbpAdminDatabaseProvider.Sqlite, StringComparison.OrdinalIgnoreCase))
        {
            EnsureSqliteDatabase(connectionString);
            return;
        }

        throw new NotSupportedException(
            $"Runtime tenant database creation does not support provider '{AbpAdminDatabaseProvider.Get(_configuration)}'.");
    }

    private async Task EnsurePostgreSqlDatabaseAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            // 明确失败优于拼出 CREATE DATABASE ""（语法错误，且被处理器吞掉只剩日志）
            throw new ArgumentException("PostgreSQL tenant connection string is missing 'Database'.");
        }

        // 连维护库 postgres 检查/建库：宿主与租户库同 DBMS，服务器地址取自租户连接串本身
        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT 1 FROM pg_database WHERE datname = $1";
        existsCommand.Parameters.AddWithValue(databaseName);
        if (await existsCommand.ExecuteScalarAsync() != null)
        {
            // Debug：ensure 语义下的常态分支（重复保存/重试），避免淹没 Info 流
            Logger.LogDebug("Tenant database {DatabaseName} already exists, skipping creation.", databaseName);
            return;
        }

        await using var createCommand = connection.CreateCommand();
        // 库名来自连接串（标识符，非值）：双引号转义后拼接，杜绝经库名的注入面
        createCommand.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\"";
        await createCommand.ExecuteNonQueryAsync();

        Logger.LogInformation("Tenant database {DatabaseName} created.", databaseName);
    }

    private void EnsureSqliteDatabase(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new ArgumentException("SQLite tenant connection string is missing 'Data Source'.");
        }

        var fullPath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            // Debug：ensure 语义下的常态分支（重复保存/重试）
            Logger.LogDebug("SQLite tenant database file {Path} already exists, skipping creation.", fullPath);
            return;
        }

        // Microsoft.Data.Sqlite 在首次打开连接时创建空库文件；schema 由后续 EF 迁移填充
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        Logger.LogInformation("SQLite tenant database file {Path} created (empty; schema comes from EF migrations).", fullPath);
    }
}
