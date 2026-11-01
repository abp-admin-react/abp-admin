using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// <see cref="IAuditLogEntityChangeQueries"/> 的 EF Core 实现。
/// Include(PropertyChanges) 一次取回变更明细；AsNoTracking 纯读（回滚只读原始值，
/// 对目标实体的修改走它自己的仓储），租户过滤沿用 AbpEntityChanges 的 IMultiTenant 全局筛选。
/// </summary>
public class EfCoreAuditLogEntityChangeQueries : IAuditLogEntityChangeQueries, ITransientDependency
{
    private readonly IDbContextProvider<AbpAdminDbContext> _dbContextProvider;

    public EfCoreAuditLogEntityChangeQueries(IDbContextProvider<AbpAdminDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<EntityChange?> FindWithPropertiesAsync(Guid entityChangeId)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Set<EntityChange>().AsNoTracking()
            .Include(c => c.PropertyChanges)
            .FirstOrDefaultAsync(c => c.Id == entityChangeId);
    }
}
