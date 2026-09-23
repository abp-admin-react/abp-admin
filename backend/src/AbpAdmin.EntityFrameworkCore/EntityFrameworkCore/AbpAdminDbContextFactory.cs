using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace AbpAdmin.EntityFrameworkCore;

/* 设计期构造 DbContext。运行时建表以本工程 Sql/postgresql 与 Sql/sqlite 为准，不再生成 EF 迁移。 */
public class AbpAdminDbContextFactory : IDesignTimeDbContextFactory<AbpAdminDbContext>
{
    public AbpAdminDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();

        AbpAdminEfCoreEntityExtensionMappings.Configure();

        var builder = new DbContextOptionsBuilder<AbpAdminDbContext>();
        var connectionString = configuration.GetConnectionString("Default");
        if (AbpAdminDatabaseProvider.IsPostgreSql(configuration))
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            builder.UseNpgsql(connectionString);
        }
        else
        {
            builder.UseSqlite(connectionString);
        }

        return new AbpAdminDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        // 单一配置源：数据库配置只维护在 HttpApi.Host 的 appsettings（+secrets），dotnet ef 工具随读随用；
        // 环境变量最后加入，优先级最高（可用 Database__Provider / ConnectionStrings__Default 临时切换）。
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../AbpAdmin.HttpApi.Host/"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.secrets.json", optional: true)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}
