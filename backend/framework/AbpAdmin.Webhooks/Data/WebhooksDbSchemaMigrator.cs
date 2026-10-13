using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Webhooks.Data;

/// <summary>
/// 框架迁移扫描约定点：执行本模块 DbContext 的 EF Core 迁移（Migrations/ 目录，
/// 记账在 <see cref="AbpAdminWebhooksConsts.SchemaHistoryTable"/>，与框架两本账）。
/// <see cref="HasPendingAsync"/> 供宿主启动检查。DbContext 经容器解析（非构造注入），
/// 租户循环下的连接串切换照常生效。
/// </summary>
public class WebhooksDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public WebhooksDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<WebhooksDbContext>();

        await EnsureHistoryTableAsync(dbContext);

        await dbContext.Database.MigrateAsync();
    }

    /// <summary>
    /// 确保 History 表存在（幂等）。Npgsql 的 applied-migrations 查询对缺失表直接 42P01
    /// （EF 基类只在创建脚本路径做 Exists 检查），空库首启会炸在检查本身；
    /// 表结构对齐 Npgsql MigrationsHistoryRepository 的默认定义（varchar(150)/varchar(32)）。
    /// </summary>
    private async Task EnsureHistoryTableAsync(WebhooksDbContext dbContext)
    {
        var db = dbContext.Database.GetDbConnection();
        var opened = false;
        if (db.State != System.Data.ConnectionState.Open)
        {
            await db.OpenAsync();
            opened = true;
        }

        try
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {WebhooksHistoryTableSql} (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK_{AbpAdminWebhooksConsts.SchemaHistoryTable}" PRIMARY KEY ("MigrationId")
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (opened)
            {
                await db.CloseAsync();
            }
        }
    }

    /// <summary>
    /// History 表的 schema 限定 SQL 名（schema 来自模块配置的解析结果，与 DbContext options 同源）。
    /// schema 名做 PG 标识符双引号转义（与 TenantDatabaseCreator 的建库 DDL 同款）：DDL 里的
    /// 标识符不能走参数化，`"` → `""` 转义后任意字符集都安全。来源虽是管理员配置面
    /// （连接串 SearchPath 解析），不因信任层级省略防线——配置注入/多租户动态连接串场景下
    /// 该值即离开受信面。internal 供 AbpAdmin.EntityFrameworkCore.Tests 钉住转义契约。
    /// </summary>
    internal static string WebhooksHistoryTableSql =>
        string.IsNullOrWhiteSpace(WebhooksHistorySchema)
            ? $"\"{AbpAdminWebhooksConsts.SchemaHistoryTable}\""
            : $"\"{WebhooksHistorySchema.Replace("\"", "\"\"")}\".\"{AbpAdminWebhooksConsts.SchemaHistoryTable}\"";

    /// <summary>由宿主模块注入（ConfigureServices 时解析），默认 public。</summary>
    public static string? WebhooksHistorySchema { get; set; }

    public async Task<bool> HasPendingAsync()
    {
        // 库不存在 / History 表缺失时会抛错，调用方（宿主启动检查）统一视为「需要迁移」
        var dbContext = _serviceProvider.GetRequiredService<WebhooksDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }
}
