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
/// 从本程序集位置向上锚定仓库根（AbpAdmin.slnx），依次叠加 HttpApi.Host/appsettings.json、
/// HttpApi.Host/appsettings.secrets.json、DbMigrator/appsettings.json。
/// 生成/校验迁移的标准命令（仓库根执行）：
/// <code>
/// dotnet ef migrations add Initial
///   --project backend/src/AbpAdmin.EntityFrameworkCore
///   --startup-project backend/src/AbpAdmin.DbMigrator
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
                "设计时迁移需要 ConnectionStrings:Default（HttpApi.Host/appsettings.json）。");

        var optionsBuilder = new DbContextOptionsBuilder<AbpAdminDbContext>()
            .UseNpgsql(connectionString);

        return new AbpAdminDbContext(optionsBuilder.Options);
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
