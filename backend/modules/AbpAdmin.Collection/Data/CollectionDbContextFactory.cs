using System;
using System.IO;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.Collection.Data;

/// <summary>
/// 设计时工厂（dotnet ef）：无需启动 ABP 宿主即可构建 <see cref="CollectionDbContext"/>。
/// History 表 schema 按连接串 SearchPath 解析（MigrationHistorySchemaResolver，框架 EF 基建层共用），
/// 避免连接串带 SearchPath 时启动报 42P07。标准命令（仓库根执行）：
/// <code>
/// dotnet ef migrations add Initial
///   --project backend/modules/AbpAdmin.Collection
///   --startup-project backend/framework/AbpAdmin.DbMigrator
///   --context CollectionDbContext
/// </code>
/// </summary>
public class CollectionDbContextFactory : IDesignTimeDbContextFactory<CollectionDbContext>
{
    public CollectionDbContext CreateDbContext(string[] args)
    {
        // 与运行时同一 Npgsql 兼容开关，保证设计时模型与运行时一致
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "设计时迁移需要 ConnectionStrings:Default——按 Configuration/Database.json.example 配好数据库连接串（或临时用环境变量 ConnectionStrings__Default）。");

        var (historySchema, baseConn) = AbpAdmin.EntityFrameworkCore.MigrationHistorySchemaResolver.Resolve(connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseNpgsql(baseConn, npgsql =>
            {
                npgsql.MigrationsHistoryTable(CollectionConsts.SchemaHistoryTable, historySchema);
            });

        return new CollectionDbContext(optionsBuilder.Options);
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
