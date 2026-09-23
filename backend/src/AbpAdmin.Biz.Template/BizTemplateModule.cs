using AbpAdmin.Biz.Template.Data;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 自包含业务模块样板（Admin.NET 式"业务工程"的 ABP 形态）：
/// 实体/服务/权限/本地化/DbContext/迁移全在本模块（含双提供程序迁移程序集）。
/// 宿主侧接线只有两处——csproj 引用 + 一行 DependsOn；DbMigrator 同样两处，
/// 框架迁移循环会自动枚举模块的 IAbpAdminDbSchemaMigrator 实现完成建表。
/// 新增业务：复制本模块三个工程改名，替换 BizTemplate 词根即可。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreModule), // EF 基建 + IAbpAdminDbSchemaMigrator 约定接口
    typeof(AbpAspNetCoreMvcModule),            // Auto API：ConventionalControllers
    typeof(AbpAuthorizationModule),            // 权限定义
    typeof(AbpLocalizationModule)
)]
public class AbpAdminBizTemplateModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var usePostgreSql = AbpAdminDatabaseProvider.IsPostgreSql(configuration);

        context.Services.AddAbpDbContext<BizTemplateDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        // 显式注册迁移器：框架迁移循环按 IAbpAdminDbSchemaMigrator 接口枚举。
        // 不依赖约定暴露（ITransientDependency 的接口自动暴露在跨程序集场景下不保证），
        // 显式一行换来确定被扫到——这是模块接线契约的一部分。
        context.Services.AddTransient<IAbpAdminDbSchemaMigrator, BizTemplateDbSchemaMigrator>();

        // 只按上下文覆盖本模块，不触碰宿主 AbpAdminDbContext 的全局提供程序配置；
        // History 表独立（__BizTemplateMigrations）——框架/业务两套迁移各记各的账
        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<BizTemplateDbContext>(ctx =>
            {
                if (usePostgreSql)
                {
                    ctx.UseNpgsql(npgsql =>
                    {
                        npgsql.MigrationsAssembly(BizTemplateMigrationsAssemblies.PostgreSql);
                        npgsql.MigrationsHistoryTable("__BizTemplateMigrations");
                    });
                }
                else
                {
                    ctx.UseSqlite(sqlite =>
                    {
                        sqlite.MigrationsAssembly(BizTemplateMigrationsAssemblies.Sqlite);
                        sqlite.MigrationsHistoryTable("__BizTemplateMigrations");
                    });
                }
            });
        });

        // 模块服务自动暴露为 REST API（/api/app/biz-project）
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AbpAdminBizTemplateModule).Assembly);
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<BizTemplateResource>("en")
                .AddVirtualJson("/Localization/BizTemplate");
        });

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminBizTemplateModule>();
        });
    }
}
