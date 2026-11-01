using System;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// <see cref="ITenantDatabaseCreator"/> 的实现：建独立租户库（PostgreSQL），
/// 租户库与 host 同 DBMS（连接串管理界面的语义约定）。
/// 连维护库 postgres 查 pg_database，缺失则 CREATE DATABASE；schema 由
/// <see cref="IAbpAdminDbSchemaMigrator"/> 的 EF Core 迁移补齐。
/// 日志契约：新建库记 Information（带库名，不含凭据）；已存在走 ensure 常态分支
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
        await EnsurePostgreSqlDatabaseAsync(connectionString);
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
}
