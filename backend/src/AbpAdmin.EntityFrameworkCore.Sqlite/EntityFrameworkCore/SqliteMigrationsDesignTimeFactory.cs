using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AbpAdmin.EntityFrameworkCore;

/*
 * 仅服务于 dotnet ef 设计期工具（migrations add / remove / database update），
 * 以本迁移程序集同时作为 --startup-project 和 --project 运行（在 backend/src 下执行）：
 *   dotnet ef migrations add Initial \
 *     --project AbpAdmin.EntityFrameworkCore.Sqlite \
 *     --startup-project AbpAdmin.EntityFrameworkCore.Sqlite
 * 连接串是占位符（本机不会真正连接）；provider 固定 Sqlite，与所在程序集一一对应。
 * 运行时迁移与本类无关：由 DbMigrator 或 HttpApi.Host（Database:AutoMigrateOnStartup）
 * 按 AbpAdminEntityFrameworkCoreModule 的 MigrationsAssembly 配置执行。
 */
public class SqliteMigrationsDesignTimeFactory : IDesignTimeDbContextFactory<AbpAdminDbContext>
{
    public AbpAdminDbContext CreateDbContext(string[] args)
    {
        AbpAdminEfCoreEntityExtensionMappings.Configure();

        var builder = new DbContextOptionsBuilder<AbpAdminDbContext>();
        builder.UseSqlite("Data Source=design-time-placeholder.db",
            sqlite => sqlite.MigrationsAssembly(AbpAdminMigrationsAssemblies.Sqlite));

        return new AbpAdminDbContext(builder.Options);
    }
}
