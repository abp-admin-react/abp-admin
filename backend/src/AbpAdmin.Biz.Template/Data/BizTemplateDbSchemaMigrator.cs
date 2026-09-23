using System;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 框架迁移扫描约定点：AbpAdminDbMigrationService 枚举容器内所有
/// IAbpAdminDbSchemaMigrator 实现逐个执行——业务模块提供本实现即被自动扫到，
/// 框架 DbMigrator/Host 零改动。迁移程序集与 History 表由 BizTemplateModule
/// 的 AbpDbContextOptions（按上下文覆盖）指定，与宿主迁移两本账互不干扰。
/// </summary>
public class BizTemplateDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BizTemplateDbSchemaMigrator> _logger;

    public BizTemplateDbSchemaMigrator(
        IServiceProvider serviceProvider,
        ILogger<BizTemplateDbSchemaMigrator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task MigrateAsync()
    {
        _logger.LogInformation("BizTemplate 业务库迁移开始（History: __BizTemplateMigrations）...");

        // 与框架 EntityFrameworkCoreAbpAdminDbSchemaMigrator 同一手法：经容器解析，
        // 让租户循环下的连接串切换照常生效
        await _serviceProvider
            .GetRequiredService<BizTemplateDbContext>()
            .Database
            .MigrateAsync();

        _logger.LogInformation("BizTemplate 业务库迁移完成。");
    }
}
