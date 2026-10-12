using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection.Data;

/// <summary>
/// 框架迁移扫描约定点：执行本模块 DbContext 的 EF Core 迁移（Migrations/ 目录，
/// 记账在 <see cref="CollectionConsts.SchemaHistoryTable"/>，与框架两本账）。
/// <see cref="HasPendingAsync"/> 供宿主启动检查。DbContext 经容器解析（非构造注入）。
/// </summary>
public class CollectionDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public CollectionDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<CollectionDbContext>();
        await EnsureHistoryTableAsync(dbContext);
        await dbContext.Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        // 库不存在 / History 表缺失时会抛错，调用方（宿主启动检查）统一视为「需要迁移」
        var dbContext = _serviceProvider.GetRequiredService<CollectionDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }

    /// <summary>
    /// 确保 History 表存在（幂等），表结构对齐 Npgsql MigrationsHistoryRepository 默认定义——
    /// 与 Webhooks 模块同款：空库首启时 EF 基类只在创建脚本路径做 Exists 检查，
    /// applied-migrations 查询对缺失表直接 42P01。
    /// </summary>
    private static async Task EnsureHistoryTableAsync(CollectionDbContext dbContext)
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
                CREATE TABLE IF NOT EXISTS "{HistorySchema}"."{CollectionConsts.SchemaHistoryTable}" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK_{CollectionConsts.SchemaHistoryTable}" PRIMARY KEY ("MigrationId")
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

    /// <summary>History 表 schema（静态注入，模块接线时按连接串 SearchPath 解析，仅迁移路径使用）。</summary>
    public static string HistorySchema { get; set; } = AbpAdmin.EntityFrameworkCore.MigrationHistorySchemaResolver.DefaultSchema;
}
