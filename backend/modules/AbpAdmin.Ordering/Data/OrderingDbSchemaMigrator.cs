using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ordering.Data;

/// <summary>
/// 框架迁移扫描约定点：执行本模块 DbContext 的 EF Core 迁移
///（记账在 <see cref="OrderingConsts.SchemaHistoryTable"/>）。
/// <see cref="HasPendingAsync"/> 供宿主启动检查。DbContext 经容器解析（非构造注入）。
/// </summary>
public class OrderingDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public OrderingDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<OrderingDbContext>();
        await EnsureHistoryTableAsync(dbContext);
        await dbContext.Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<OrderingDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }

    /// <summary>确保 History 表存在（幂等），与 Webhooks/Collection 同款（空库首启 42P01 防护）。</summary>
    private static async Task EnsureHistoryTableAsync(OrderingDbContext dbContext)
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
                CREATE TABLE IF NOT EXISTS "{HistorySchema}"."{OrderingConsts.SchemaHistoryTable}" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK_{OrderingConsts.SchemaHistoryTable}" PRIMARY KEY ("MigrationId")
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
