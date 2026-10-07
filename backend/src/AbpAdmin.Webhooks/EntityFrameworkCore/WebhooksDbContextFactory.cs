using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.Webhooks.EntityFrameworkCore;

/// <summary>
/// 设计时工厂（dotnet ef）：无需启动宿主即可构建 <see cref="WebhooksDbContext"/>。
/// 配置读取与 DbMigrator 同源——从程序集位置向上锚定仓库根（AbpAdmin.slnx），
/// 依次叠加 HttpApi.Host/appsettings.json、secrets、DbMigrator/appsettings.json。
/// 标准命令（仓库根执行）：
/// <code>
/// dotnet ef migrations add Initial
///   --project backend/src/AbpAdmin.Webhooks
///   --startup-project backend/src/AbpAdmin.DbMigrator
///   --context WebhooksDbContext
/// </code>
/// </summary>
public class WebhooksDbContextFactory : IDesignTimeDbContextFactory<WebhooksDbContext>
{
    public WebhooksDbContext CreateDbContext(string[] args)
    {
        // 与运行时（AbpAdminEntityFrameworkCoreModule）同一 Npgsql 兼容开关，保证设计时
        // 生成的迁移与运行时模型在 timestamp 映射上完全一致
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var configuration = BuildConfiguration();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "设计时迁移需要 ConnectionStrings:Default（HttpApi.Host/appsettings.json）。");

        var (historySchema, baseConn) = WebhooksHistorySchemaResolver.Resolve(connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseNpgsql(baseConn, npgsql =>
            {
                npgsql.MigrationsHistoryTable(AbpAdminWebhooksConsts.SchemaHistoryTable, historySchema);
            });

        return new WebhooksDbContext(optionsBuilder.Options);
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
