using System;
using System.IO;
using AbpAdmin.Biz.Template.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 设计时工厂（dotnet ef）：让 `dotnet ef migrations add/has-pending-model-changes` 无需启动
/// ABP 宿主即可构建 <see cref="BizTemplateDbContext"/>。配置读取与框架
/// <c>AbpAdminDbContextFactory</c> 同源（HttpApi.Host → secrets → DbMigrator 分层）。
/// History 表名与运行时保持一致（__BizTemplate_EFMigrationsHistory）。
/// 生成迁移的标准命令（仓库根执行）：
/// <code>
/// dotnet ef migrations add Initial
///   --project backend/src/AbpAdmin.Biz.Template
///   --startup-project backend/src/AbpAdmin.DbMigrator
///   --context BizTemplateDbContext
/// </code>
/// </summary>
public class BizTemplateDbContextFactory : IDesignTimeDbContextFactory<BizTemplateDbContext>
{
    public BizTemplateDbContext CreateDbContext(string[] args)
    {
        // 与框架工厂同一 Npgsql 兼容开关，保证设计时模型与运行时一致
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "设计时迁移需要 ConnectionStrings:Default（HttpApi.Host/appsettings.json）。");

        var optionsBuilder = new DbContextOptionsBuilder<BizTemplateDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable(BizTemplateConsts.SchemaHistoryTable);
            });

        return new BizTemplateDbContext(optionsBuilder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                $"未能定位仓库根（AbpAdmin.slnx）——设计时工厂从 '{AppContext.BaseDirectory}' 向上查找失败。");

        return new ConfigurationBuilder()
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "src", "AbpAdmin.HttpApi.Host", "appsettings.json"),
                optional: false, reloadOnChange: false)
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "src", "AbpAdmin.HttpApi.Host", "appsettings.secrets.json"),
                optional: true, reloadOnChange: false)
            .AddJsonFile(
                Path.Combine(repoRoot.FullName, "src", "AbpAdmin.DbMigrator", "appsettings.json"),
                optional: true, reloadOnChange: false)
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
