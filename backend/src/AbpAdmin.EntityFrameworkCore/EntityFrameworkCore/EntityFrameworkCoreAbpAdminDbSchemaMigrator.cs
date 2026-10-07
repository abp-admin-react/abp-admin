using System;
using System.Linq;
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

/// <summary>
/// 框架库 schema 迁移器（IAbpAdminDbSchemaMigrator 实现，被迁移循环自动枚举）。两步：
/// ① EnsureHostDatabaseExistsAsync——PG 库缺失时经维护库自动建库；
/// ② AbpAdminDbContext.Database.MigrateAsync()——执行本工程 Migrations/ 下的 EF Core 迁移
///    （QRTZ_ 表的建表 DDL 已随一次性迁移进 __EFMigrationsHistory 记账，无独立执行步骤）。
/// <see cref="HasPendingAsync"/> 供宿主启动检查：History 表与当前模型相比是否还有未应用迁移。
/// DbContext 经容器解析（非构造注入），租户循环下的连接串切换照常生效。
/// </summary>
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

        await _serviceProvider.GetRequiredService<AbpAdminDbContext>()
            .Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        // 库不存在 / History 表缺失时 GetPendingMigrationsAsync 会抛错，
        // 调用方（宿主启动检查）统一视为「需要迁移」——与接口契约一致
        var dbContext = _serviceProvider.GetRequiredService<AbpAdminDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }

    /// <summary>
    /// 全自动建库（仅宿主库）：目标库不存在时通过连接串同源的维护库 postgres
    /// 执行 CREATE DATABASE，要求登录角色具备 CREATEDB 权限且 pg_hba 放行 postgres 库。
    /// 检测不到（维护库连不上）时降级为警告，交由后续迁移以原生错误暴露真实原因。
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

        var logger = _serviceProvider
            .GetRequiredService<ILogger<EntityFrameworkCoreAbpAdminDbSchemaMigrator>>();

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            // 与 TenantDatabaseCreator 同一守卫：缺库名时明确失败，优于 AddWithValue(null) 查不到、
            // 再被下方 catch 折叠成"跳过自动建库"的误导性警告
            throw new ArgumentException("PostgreSQL 'Default' 连接串缺少 'Database'，无法自动建库。");
        }

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
}
