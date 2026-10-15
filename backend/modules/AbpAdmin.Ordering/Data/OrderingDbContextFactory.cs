using System;
using System.IO;
using AbpAdmin.Ordering.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.Ordering.Data;

/// <summary>
/// 设计时工厂（dotnet ef）。History 表 schema 按连接串 SearchPath 解析（共用解析器）。
/// 标准命令（仓库根执行）：
/// <code>
/// dotnet ef migrations add Initial
///   --project backend/modules/AbpAdmin.Ordering
///   --startup-project backend/framework/AbpAdmin.DbMigrator
///   --context OrderingDbContext
/// </code>
/// </summary>
public class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "设计时迁移需要 ConnectionStrings:Default——按 Configuration/Database.json.example 配好数据库连接串（或临时用环境变量 ConnectionStrings__Default）。");

        var (historySchema, baseConn) = AbpAdmin.EntityFrameworkCore.MigrationHistorySchemaResolver.Resolve(connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(baseConn, npgsql =>
            {
                npgsql.MigrationsHistoryTable(OrderingConsts.SchemaHistoryTable, historySchema);
            });

        return new OrderingDbContext(optionsBuilder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                $"未能定位仓库根（AbpAdmin.slnx）——设计时工厂从 '{AppContext.BaseDirectory}' 向上查找失败。");

        return new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "framework", "AbpAdmin.HttpApi.Host", "appsettings.json"),
                optional: false, reloadOnChange: false)
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "framework", "AbpAdmin.HttpApi.Host", "Configuration", "Database.json"),
                optional: true, reloadOnChange: false)
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "framework", "AbpAdmin.HttpApi.Host", "appsettings.secrets.json"),
                optional: true, reloadOnChange: false)
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "framework", "AbpAdmin.DbMigrator", "appsettings.json"),
                optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    private static DirectoryInfo? FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AbpAdmin.slnx")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
