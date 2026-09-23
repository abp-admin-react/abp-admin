using AbpAdmin.Biz.Template.Data;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 自包含业务模块样板（一个工程，对齐 Admin.NET.Application 的摆放方式）：
/// 实体/服务/权限/本地化/DbContext，以及 PostgreSQL、SQLite 两份 SQL 脚本都在本模块。
/// 宿主接线是 csproj 引用 + 一行 DependsOn；DbMigrator 同样只引用本工程。
/// 框架迁移循环与宿主启动待办检查会自动枚举模块的 IAbpAdminDbSchemaMigrator（MigrateAsync / HasPendingAsync），
/// 按 Database:Provider 执行对应脚本。
/// 新增业务：复制本工程改名，替换 BizTemplate 词根即可。
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
        context.Services.AddAbpDbContext<BizTemplateDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        // 显式注册迁移器：框架迁移循环按 IAbpAdminDbSchemaMigrator 接口枚举。
        // 不依赖约定暴露（ITransientDependency 的接口自动暴露在跨程序集场景下不保证），
        // 显式一行换来确定被扫到——这是模块接线契约的一部分。
        // 提供程序沿用框架 AbpDbContextOptions 的 Database:Provider，本模块不再单独指定迁移程序集。
        context.Services.AddTransient<IAbpAdminDbSchemaMigrator, BizTemplateDbSchemaMigrator>();

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
