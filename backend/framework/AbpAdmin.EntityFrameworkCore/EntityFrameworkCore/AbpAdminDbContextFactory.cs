using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 设计时工厂（dotnet ef）：让 `dotnet ef migrations add/has-pending-model-changes` 无需启动
/// ABP 宿主即可构建 <see cref="AbpAdminDbContext"/>。配置读取与 DbMigrator 的分层同源——
/// 从本程序集位置向上锚定 backend 根（AbpAdmin.slnx），依次叠加 HttpApi.Host/appsettings.json、
/// Configuration/Database.json、HttpApi.Host/appsettings.secrets.json、DbMigrator/appsettings.json、环境变量。
/// 生成/校验迁移的标准命令（仓库根执行）：
/// <code>
/// dotnet ef migrations add Initial
///   --project backend/framework/AbpAdmin.EntityFrameworkCore
///   --startup-project backend/framework/AbpAdmin.DbMigrator
///   --context AbpAdminDbContext
/// </code>
/// </summary>
public class AbpAdminDbContextFactory : IDesignTimeDbContextFactory<AbpAdminDbContext>
{
    public AbpAdminDbContext CreateDbContext(string[] args)
    {
        AbpAdminEfCoreEntityExtensionMappings.Configure();
        // 与运行时（AbpAdminEntityFrameworkCoreModule）同一 Npgsql 兼容开关，保证设计时
        // 生成的迁移与运行时模型在 timestamp 映射上完全一致
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "设计时迁移需要 ConnectionStrings:Default——按 Configuration/Database.json.example 配好数据库连接串" +
                "（或临时用环境变量 ConnectionStrings__Default）。");

        var optionsBuilder = new DbContextOptionsBuilder<AbpAdminDbContext>()
            .UseNpgsql(connectionString);

        return new AbpAdminDbContext(optionsBuilder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(
                $"未能定位仓库根（AbpAdmin.slnx）——设计时工厂从 '{AppContext.BaseDirectory}' 向上查找失败。");

        // 分层与宿主运行时同源（OCR design 修复：连接串真源已拆到 Configuration/Database.json，
        // 此前工厂只读 appsettings.json + secrets，照新模板配置的开发者跑 dotnet ef 直接扑空）
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
