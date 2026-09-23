using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AbpAdmin.EntityFrameworkCore;

/*
 * 仅服务于 dotnet ef 设计期工具（migrations add / remove / database update），
 * 以本迁移程序集同时作为 --startup-project 和 --project 运行（在 backend/src 下执行）：
 *   dotnet ef migrations add Initial \
 *     --project AbpAdmin.EntityFrameworkCore.PostgreSql \
 *     --startup-project AbpAdmin.EntityFrameworkCore.PostgreSql
 * 连接串是占位符（本机不会真正连接）；provider 固定 PostgreSql，与所在程序集一一对应。
 * 运行时迁移与本类无关：由 DbMigrator 或 HttpApi.Host（Database:AutoMigrateOnStartup）
 * 按 AbpAdminEntityFrameworkCoreModule 的 MigrationsAssembly 配置执行。
 */
public class PostgreSqlMigrationsDesignTimeFactory : IDesignTimeDbContextFactory<AbpAdminDbContext>
{
    public AbpAdminDbContext CreateDbContext(string[] args)
    {
        AbpAdminEfCoreEntityExtensionMappings.Configure();
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var builder = new DbContextOptionsBuilder<AbpAdminDbContext>();
        builder.UseNpgsql("Host=design-time-placeholder;Database=placeholder;Username=p;Password=p",
            npgsql => npgsql.MigrationsAssembly(AbpAdminMigrationsAssemblies.PostgreSql));

        return new AbpAdminDbContext(builder.Options);
    }
}
