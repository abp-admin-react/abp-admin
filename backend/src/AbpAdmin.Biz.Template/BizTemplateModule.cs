using System;
using AbpAdmin.Biz.Template.Configuration;
using AbpAdmin.Biz.Template.Data;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
///
/// 模块配置按三层落位（对应实现都带注释，照着扩）：
/// ① 基础设施配置（连接串/Redis/端口）——只属于宿主 appsettings.json（框架配置唯一来源），模块只读不写；
/// ② 模块部署期配置——Configuration/BizTemplateOptions + 同目录 BizTemplate.json：
///    换环境要变、改完要重启的模块自有配置。基线随模块走（Content 复制到输出目录），
///    在下方 ConfigureServices 里绑定；宿主 IConfiguration 的同名节（secrets/环境变量汇入于此）
///    可选覆盖——宿主文件零改动。Admin.NET 把 Database/JWT 等也堆在业务工程 Configuration\ 里，
///    本样板只放业务配置，框架与业务的边界按配置归属物理隔离，比它更彻底。
/// ③ 运行期业务参数——Settings/BizTemplateSettingDefinitionProvider：设置页可调、免重启，
///    定义随模块自持，存储复用框架 SettingManagement（对应 Admin.NET SysConfig 表的角色，无需建表）。
/// 判定一句话：换环境变的进 ②，管理员日常调的进 ③，底座运转的在 ①（不归业务管）。
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
        // ── 第②层：模块部署期配置绑定（配置三层约定，见类注释）────────────────────
        // 基线：模块自带的 Configuration/BizTemplate.json，从模块自己的输出目录读
        //（AppContext.BaseDirectory 在宿主里即宿主 bin，Content 复制已把它带过来），
        // optional:false——文件缺失直接启动失败（fail-fast 优于悄悄用空配置）。
        // 【约束】self-contained 单文件发布下 BaseDirectory 与松散 Content 文件会分离，
        // 本读取方式不兼容该发布形态（宿主现有发布是普通目录布局，不受影响）。
        // 覆盖：宿主 IConfiguration 的同名节（secrets/环境变量汇入于此）。先绑基线再绑宿主节，
        // 后绑定生效；宿主侧没配这个节时 Bind 是 no-op，宿主零改动照常运行。
        var hostConfiguration = context.Services.GetConfiguration();
        var moduleBaseline = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("Configuration/BizTemplate.json", optional: false, reloadOnChange: false)
            .Build();

        // 节存在性守卫：optional:false 只保证"文件在"，不保证"根节名对"——复制模块改名时
        // 只改了 JSON 根节没改 SectionName（或反之）会静默退化成 C# 默认值（含生产里悄悄
        // 播种样例数据），这里把错配提前到启动即炸，错误信息带上改名清单。
        if (!moduleBaseline.GetSection(BizTemplateOptions.SectionName).Exists())
        {
            throw new InvalidOperationException(
                $"Configuration/BizTemplate.json 缺少根节 \"{BizTemplateOptions.SectionName}\"：" +
                "复制模块改名时，JSON 根节名必须与 BizTemplateOptions.SectionName 同步替换词根。");
        }

        context.Services.Configure<BizTemplateOptions>(options =>
        {
            moduleBaseline.GetSection(BizTemplateOptions.SectionName).Bind(options);
            hostConfiguration.GetSection(BizTemplateOptions.SectionName).Bind(options);
        });
        // 第③层（Settings/BizTemplateSettingDefinitionProvider）无需在此注册：
        // SettingDefinitionProvider 由 ABP 按模块程序集自动发现，DependsOn 本模块即生效。

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

            // 设置页分组名本地化：EasyAbp SettingUi 用 IStringLocalizer<SettingUiResource> 解析
            // Group1 显示名（GroupDisplayName = _localizer["BizTemplate"]），模块资源必须挂到
            // SettingUiResource 链上才能命中——框架的分组名（"Payment"→"支付"）就是这么生效的
            //（框架在 DomainSharedModule 把 /Localization/AbpAdmin 挂到了 SettingUiResource）。
            // 本行在模块内完成同款接线，不碰框架文件；模块整体移除时该贡献随之消失。
            // 设置项自身的 DisplayName/Description 不依赖这里（LocalizableString.Create<BizTemplateResource>
            // 走定义自己的资源），只有 Tab 分组名走 SettingUiResource。
            // 【随挂副作用，刻意接受】模块 JSON 里的通用键（Permission:Create 等）也会进入
            // SettingUiResource 链——当前 SettingUi 链只被用来查分组名，这些键处于惰性状态；
            // 与框架域（同样整文件挂链）行为一致。若未来 SettingUi 链开始消费通用键，需换专用最小 JSON。
            options.Resources
                .Get<EasyAbp.Abp.SettingUi.Localization.SettingUiResource>()
                .AddVirtualJson("/Localization/BizTemplate");
        });

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminBizTemplateModule>();
        });
    }
}
