using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.TokenGateway.Data;

/// <summary>
/// 设计时工厂（dotnet ef）。生成迁移不连库，给占位连接串即可：
/// <code>
/// dotnet ef migrations add &lt;Name&gt; --project backend/modules/AbpAdmin.TokenGateway
///   --startup-project backend/framework/AbpAdmin.DbMigrator --context TokenGatewayDbContext
/// </code>
/// 连接串解析与框架 AbpAdminDbContextFactory 同源（OCR design 修复：不再只读环境变量）：
/// env 优先 → 运行时真源 Configuration/Database.json → Host 基线 → DbMigrator 基线 → 占位串。
/// </summary>
public class TokenGatewayDbContextFactory : IDesignTimeDbContextFactory<TokenGatewayDbContext>
{
    public TokenGatewayDbContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var cs = ResolveConnectionString();
        var b = new DbContextOptionsBuilder<TokenGatewayDbContext>();
        TokenGatewayDbContext.ConfigureNpgsql(b, cs);
        return new TokenGatewayDbContext(b.Options);
    }

    private static string ResolveConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 锚定口径与 AbpAdminDbContextFactory 一致：AbpAdmin.slnx 在 backend/ 下，
        // 找到即 backend 根（OCR R2 修复：此前误当仓库根又拼一层 backend，三份 json 全部落空、
        // 静默永远回落占位串——Database.json 真源整段是死代码）
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AbpAdmin.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            return "Host=localhost;Database=design_time_only";
        }

        var hostDir = Path.Combine(dir.FullName, "framework", "AbpAdmin.HttpApi.Host");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(hostDir, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(hostDir, "Configuration", "Database.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(dir.FullName, "framework", "AbpAdmin.DbMigrator", "appsettings.json"), optional: true, reloadOnChange: false)
            .Build();
        return configuration.GetConnectionString("Default") ?? "Host=localhost;Database=design_time_only";
    }
}
