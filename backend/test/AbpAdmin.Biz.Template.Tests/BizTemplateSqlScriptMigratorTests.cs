using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 模块 Sql 脚本契约（随模块复制——派生项目的第一道防漂移网）：
/// 经 BizTemplateDbSchemaMigrator 真实执行本程序集 Sql/{provider} 全部脚本（而非测试基建的
/// EF 模型建表，后者会绕过脚本、掩盖"模型加列忘补脚本"的漂移），断言：
/// History 双态（缺表抛错 → 迁移后 false 记账 2 行）、BizProjects 全模型列可插可查（脚本漏列即红）。
/// </summary>
public class BizTemplateSqlScriptMigratorTests
{
    private const string HistoryTable = "__BizTemplateMigrations";

    [Fact]
    public async Task Module_Scripts_Should_Create_Schema_Matching_Model()
    {
        var dataSource = Path.Combine(Path.GetTempPath(), $"biztpl_ocr_{Guid.NewGuid():N}.db");
        try
        {
            await using var context = CreateContext(dataSource);
            var migrator = CreateMigrator(context, dataSource);

            // 空库：HasPending 按契约抛 SqliteException（History 表不存在）——调用方统一视为"需要迁移"
            await Assert.ThrowsAsync<SqliteException>(() => migrator.HasPendingAsync());

            await migrator.MigrateAsync();

            // 记账：001 + 002 全部入账
            Assert.Equal(2, Convert.ToInt32(await ScalarAsync(context,
                $"""SELECT COUNT(*) FROM "{HistoryTable}" """)));

            // HasPending 终态：全部脚本已记账 → false
            Assert.False(await migrator.HasPendingAsync());

            // 模型↔脚本列集对账：按实体全列插入（脚本漏任一列即 SQL 错误变红）
            await ExecuteAsync(context, """
                INSERT INTO "BizProjects"
                    ("Id", "Name", "Description", "IsActive", "ExtraProperties", "ConcurrencyStamp",
                     "CreationTime", "CreatorId", "LastModificationTime", "LastModifierId")
                VALUES
                    ('4f1a2d3e-0000-0000-0000-000000000001',
                     'OCR 列集对账', '模型与脚本漂移守卫', 1, '{}', 'ocr-stamp',
                     '2026-09-24 00:00:00', NULL, NULL, NULL);
                """);
            Assert.Equal(1, Convert.ToInt32(await ScalarAsync(context,
                """SELECT COUNT(*) FROM "BizProjects" """)));

            // 迁移幂等：重跑零新增（记账去重）
            await migrator.MigrateAsync();
            Assert.Equal(2, Convert.ToInt32(await ScalarAsync(context,
                $"""SELECT COUNT(*) FROM "{HistoryTable}" """)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dataSource))
            {
                File.Delete(dataSource);
            }
        }
    }

    private static BizTemplateDbContext CreateContext(string dataSource)
    {
        return new BizTemplateDbContext(
            new DbContextOptionsBuilder<BizTemplateDbContext>()
                .UseSqlite($"Data Source={dataSource}")
                .Options);
    }

    private static BizTemplateDbSchemaMigrator CreateMigrator(BizTemplateDbContext context, string dataSource)
    {
        // 迁移器经容器解析 DbContext（保持与生产同路径）；配置只给 Database:Provider 决定脚本目录
        var services = new ServiceCollection().AddSingleton(context).BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Sqlite" })
            .Build();

        return new BizTemplateDbSchemaMigrator(
            services, configuration, NullLogger<BizTemplateDbSchemaMigrator>.Instance);
    }

    private static async Task<object?> ScalarAsync(BizTemplateDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await context.Database.OpenConnectionAsync();
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(BizTemplateDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await context.Database.OpenConnectionAsync();
        await command.ExecuteNonQueryAsync();
    }
}
