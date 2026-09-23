using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AbpAdmin.Data;
using Npgsql;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.EntityFrameworkCore;

public class EntityFrameworkCoreAbpAdminDbSchemaMigrator
    : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreAbpAdminDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the AbpAdminDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await EnsureHostDatabaseExistsAsync();

        await _serviceProvider
            .GetRequiredService<AbpAdminDbContext>()
            .Database
            .MigrateAsync();

        // T3.3 第 3 步：EF 迁移之后、数据种子之前确保 QRTZ_ 表存在。
        // 不混进 EF 迁移——QRTZ_ schema 归 Quartz 上游所有，DDL 随上游版本变化。
        await EnsureQuartzTablesAsync();
    }

    /// <summary>
    /// 全自动建库（仅宿主库）：目标库不存在时按当前提供程序自动创建。
    /// SQLite 由驱动自动建库文件，无需处理；PostgreSQL 通过连接串同源的维护库 postgres
    /// 执行 CREATE DATABASE，要求登录角色具备 CREATEDB 权限且 pg_hba 放行 postgres 库。
    /// 检测不到（维护库连不上）时降级为警告，交由后续 EF 迁移以原生错误暴露真实原因。
    /// </summary>
    private async Task EnsureHostDatabaseExistsAsync()
    {
        var currentTenant = _serviceProvider.GetRequiredService<ICurrentTenant>();
        if (currentTenant.Id.HasValue)
        {
            // 租户库按各自连接串处理；当前部署形态租户共享宿主库，无需单独建库
            return;
        }

        var configuration = _serviceProvider.GetRequiredService<IConfiguration>();
        if (!AbpAdminDatabaseProvider.IsPostgreSql(configuration))
        {
            return;
        }

        var logger = _serviceProvider
            .GetRequiredService<ILogger<EntityFrameworkCoreAbpAdminDbSchemaMigrator>>();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabase = builder.Database;
        builder.Database = "postgres";

        bool exists;
        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            await using var query = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = $1", connection);
            query.Parameters.AddWithValue(targetDatabase);
            exists = await query.ExecuteScalarAsync() is not null;

            if (exists)
            {
                return;
            }

            logger.LogInformation("数据库 {Database} 不存在，自动创建...", targetDatabase);

            // CREATE DATABASE 不能在事务内执行，单独一条命令发出
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{targetDatabase.Replace("\"", "\"\"")}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            // 42501 insufficient_privilege：登录角色缺少 CREATEDB 权限（CREATE DATABASE 的标准报错）
            throw new InvalidOperationException(
                $"自动创建数据库 {targetDatabase} 失败：{ex.MessageText}。" +
                "请为登录角色授权 CREATEDB（ALTER ROLE <user> CREATEDB），或由 DBA 预建目标库后重跑。",
                ex);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "无法通过维护库(postgres)检查数据库 {Database} 是否存在，跳过自动建库。" +
                "若需启用全自动建库：① pg_hba.conf 放行维护库与目标库（如 host all <user> 0.0.0.0/0 md5）后 reload；" +
                "② 给登录角色授权 CREATEDB。若库确实缺失，后续迁移会给出具体错误",
                targetDatabase);
            return;
        }

        logger.LogInformation("数据库 {Database} 创建完成。", targetDatabase);
    }

    /// <summary>
    /// 执行 etc/quartz/tables_postgres.sql（官方脚本的幂等化版本，重跑不报错、不清数据）。
    /// 仅 <c>Quartz:UsePersistentStore = true</c> 且当前为 host 库迁移时执行：
    /// Quartz 调度器是进程级单例，只连 host 的 Default 库，租户库不需要 QRTZ_ 表。
    /// </summary>
    private async Task EnsureQuartzTablesAsync()
    {
        var configuration = _serviceProvider.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("Quartz:UsePersistentStore"))
        {
            return;
        }

        var currentTenant = _serviceProvider.GetRequiredService<ICurrentTenant>();
        if (currentTenant.Id.HasValue)
        {
            return;
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<EntityFrameworkCoreAbpAdminDbSchemaMigrator>>();

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "quartz", "tables_postgres.sql");
        if (!File.Exists(scriptPath))
        {
            logger.LogWarning("Quartz table script not found at {Path}, skipping.", scriptPath);
            return;
        }

        var sql = await File.ReadAllTextAsync(scriptPath);

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Default"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        logger.LogInformation("Quartz tables ensured.");
    }
}
