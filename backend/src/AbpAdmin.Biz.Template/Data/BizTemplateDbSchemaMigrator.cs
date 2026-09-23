using System;
using System.Threading.Tasks;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 框架迁移扫描约定点。按 <c>Database:Provider</c> 执行本程序集里的 SQL 脚本
/// （<c>Sql/postgresql</c> 或 <c>Sql/sqlite</c>，经 <see cref="EmbeddedSqlScriptMigrator"/>：
/// 每脚本一事务、History 记账、失败回滚为「未应用」），并在 <c>__BizTemplateMigrations</c> 记账。
/// <see cref="HasPendingAsync"/> 供宿主启动检查：脚本是否都已记入 History 表。
/// DbContext 经容器解析（非构造注入），租户循环下的连接串切换照常生效。
/// </summary>
public class BizTemplateDbSchemaMigrator : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BizTemplateDbSchemaMigrator> _logger;

    public BizTemplateDbSchemaMigrator(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<BizTemplateDbSchemaMigrator> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task MigrateAsync()
    {
        await EmbeddedSqlScriptMigrator.ApplyAsync(
            _serviceProvider.GetRequiredService<BizTemplateDbContext>(),
            GetScriptFolder(),
            BizTemplateConsts.SchemaHistoryTable,
            typeof(BizTemplateDbSchemaMigrator).Assembly,
            _logger,
            "BizTemplate 业务库");
    }

    public async Task<bool> HasPendingAsync()
    {
        return await EmbeddedSqlScriptMigrator.HasPendingAsync(
            _serviceProvider.GetRequiredService<BizTemplateDbContext>(),
            GetScriptFolder(),
            BizTemplateConsts.SchemaHistoryTable,
            typeof(BizTemplateDbSchemaMigrator).Assembly);
    }

    // 提供程序判定唯一入口：脚本目录名与 csproj 的 Sql\<目录> 嵌入约定绑定
    private string GetScriptFolder()
    {
        return AbpAdminDatabaseProvider.GetScriptFolder(_configuration);
    }
}
