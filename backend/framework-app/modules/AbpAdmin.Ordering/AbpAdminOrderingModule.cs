using AbpAdmin.Collection.Contracts;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Ordering;

/// <summary>
/// 自包含订单模块（单工程）。依赖方向（计划第 2 节）：Ordering → Collection.Contracts
/// （枚举/ETO/集成服务接口）；不引用收款实现工程。订单代码里不出现 EasyAbp 类型
///（经 AbpAdmin.EntityFrameworkCore 传递进来的 EasyAbp 包是框架工程既有的，登记进
/// framework-contracts.md，不作为退回条件）。
/// </summary>
[DependsOn(
    typeof(AbpAdminEntityFrameworkCoreModule), // EF 基建 + IAbpAdminDbSchemaMigrator 约定接口
    typeof(AbpAdminCollectionContractsModule), // 只看收款契约（集成服务由收款实现提供，测试用桩替换）
    typeof(AbpAspNetCoreMvcModule),            // Auto API：ConventionalControllers
    typeof(AbpAuthorizationModule),
    typeof(AbpLocalizationModule),
    typeof(AbpVirtualFileSystemModule)
)]
public class AbpAdminOrderingModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<OrderingDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        var configuration = context.Services.GetConfiguration();
        var (historySchema, _) = AbpAdmin.EntityFrameworkCore.MigrationHistorySchemaResolver.Resolve(
            configuration.GetConnectionString(OrderingConsts.ConnectionStringName)
            ?? string.Empty);
        OrderingDbSchemaMigrator.HistorySchema = historySchema;
        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<OrderingDbContext>(contextOptions =>
            {
                contextOptions.UseNpgsql(npgsql =>
                {
                    npgsql.MigrationsHistoryTable(OrderingConsts.SchemaHistoryTable, historySchema);
                });
            });
        });

        // 显式注册迁移器：框架迁移循环按 IAbpAdminDbSchemaMigrator 接口枚举（模块接线契约）
        context.Services.AddTransient<IAbpAdminDbSchemaMigrator, OrderingDbSchemaMigrator>();

        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AbpAdminOrderingModule).Assembly);
        });

        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminOrderingModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<OrderingResource>("en")
                .AddVirtualJson("/Localization/Ordering");

            // 不把模块 JSON 挂到 SettingUiResource 链：那需要 EasyAbp 类型，而订单工程
            // 不直接引用任何 EasyAbp 包（计划第 10 节，架构测试钉住）——设置页 Tab 显示
            // 裸键 "Ordering"，M3 渠道设置页落地时再在允许引用 EasyAbp 的一侧补挂
        });

        // 错误码 → 模块资源映射（BusinessException 的 code 带 "AbpAdmin.Ordering:" 前缀）
        Configure<Volo.Abp.Localization.ExceptionHandling.AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("AbpAdmin.Ordering", typeof(OrderingResource));
        });
    }
}
