using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 框架迁移扫描约定点：执行本模块 DbContext 的 EF Core 迁移（Migrations/ 目录，
/// 记账在 <c>__BizTemplate_EFMigrationsHistory</c>，与框架 <c>__EFMigrationsHistory</c> 两本账）。
/// 迁移前先做存量库自举打戳（模块 Sql/ 脚本时代建的库表在而本模块 History 缺失，
/// 见 <see cref="EfCoreLegacySchemaBaseliner"/>；哨兵用本模块自己的 BizProjects 表——
/// 不能用框架的 AbpUsers：全新库上框架迁移器先跑会先建出 AbpUsers，误判会把本模块
/// 的 Initial 也戳掉导致 BizProjects 永远建不出来）。
/// <see cref="HasPendingAsync"/> 供宿主启动检查：History 表与当前模型相比是否还有未应用迁移。
/// DbContext 经容器解析（非构造注入），租户循环下的连接串切换照常生效。
/// </summary>
public class BizTemplateDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public BizTemplateDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        var dbContext = _serviceProvider.GetRequiredService<BizTemplateDbContext>();

        await EfCoreLegacySchemaBaseliner.StampIfNeededAsync(
            dbContext.Database.GetDbConnection(),
            _serviceProvider.GetRequiredService<ILogger<BizTemplateDbSchemaMigrator>>(),
            historyTableName: BizTemplateConsts.SchemaHistoryTable,
            constraintName: "PK_" + BizTemplateConsts.SchemaHistoryTable,
            initialMigrationId: BizTemplateConsts.InitialMigrationId,
            baselineProductVersion: "10.0.9",
            sentinelTableName: "BizProjects");

        await dbContext.Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        // 库不存在 / History 表缺失时会抛错，调用方（宿主启动检查）统一视为「需要迁移」
        var dbContext = _serviceProvider.GetRequiredService<BizTemplateDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }
}
