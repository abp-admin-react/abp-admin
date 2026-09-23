using AbpAdmin.Biz.Template.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AbpAdmin.Biz.Template.EntityFrameworkCore;

/*
 * 仅服务于 dotnet ef 设计期工具（在 backend/src 下执行）：
 *   dotnet ef migrations add Initial \
 *     --project AbpAdmin.Biz.Template.Migrations.Sqlite \
 *     --startup-project AbpAdmin.Biz.Template.Migrations.Sqlite \
 *     --context BizTemplateDbContext
 * 连接串是占位符（不会真正连接）；provider 固定 Sqlite，与所在程序集一一对应。
 * 运行时迁移与本类无关：由模块 BizTemplateDbSchemaMigrator 按 BizTemplateModule
 * 的按上下文 MigrationsAssembly 配置执行（History 表 __BizTemplateMigrations 独立记账）。
 */
public class BizTemplateSqliteMigrationsDesignTimeFactory : IDesignTimeDbContextFactory<BizTemplateDbContext>
{
    public BizTemplateDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<BizTemplateDbContext>();
        builder.UseSqlite("Data Source=design-time-placeholder.db",
            sqlite =>
            {
                sqlite.MigrationsAssembly(BizTemplateMigrationsAssemblies.Sqlite);
                sqlite.MigrationsHistoryTable("__BizTemplateMigrations");
            });

        return new BizTemplateDbContext(builder.Options);
    }
}
