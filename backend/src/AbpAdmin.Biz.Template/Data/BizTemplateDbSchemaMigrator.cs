using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 框架迁移扫描约定点：执行本模块 DbContext 的 EF Core 迁移（Migrations/ 目录，
/// 记账在 <c>__BizTemplate_EFMigrationsHistory</c>，与框架 <c>__EFMigrationsHistory</c> 两本账）。
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
        await _serviceProvider.GetRequiredService<BizTemplateDbContext>()
            .Database.MigrateAsync();
    }

    public async Task<bool> HasPendingAsync()
    {
        // 库不存在 / History 表缺失时会抛错，调用方（宿主启动检查）统一视为「需要迁移」
        var dbContext = _serviceProvider.GetRequiredService<BizTemplateDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }
}
