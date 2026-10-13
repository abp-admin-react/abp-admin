using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// <see cref="IAuditLogHandledColumnQueries"/> 的 EF Core 实现：HandledAt 等是
/// Entity Extensions 映射的影子属性，只能经 EF.Property 引用（机制背景见接口注释）。
/// 列的存在性由 EfCoreAuditLogHandledColumnTests 在模型层断言，映射丢失时查询显式失败。
/// </summary>
public class EfCoreAuditLogHandledColumnQueries : IAuditLogHandledColumnQueries, ITransientDependency
{
    private readonly IDbContextProvider<AbpAdminDbContext> _dbContextProvider;

    public EfCoreAuditLogHandledColumnQueries(IDbContextProvider<AbpAdminDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    /// <summary>
    /// 追加「HandledAt IS NULL」条件。必须用 EF.Property 而不是 x.GetProperty：
    /// 后者是普通静态方法，EF 无法翻译；EF.Property 才生成单表 SQL
    /// （替换掉旧侧表方案的两段查询 + "Cannot use multiple context instances" 500）。
    /// </summary>
    public IQueryable<AuditLog> ApplyUnhandled(IQueryable<AuditLog> queryable)
        => queryable.Where(x => EF.Property<DateTime?>(x, AuditLogHandleConsts.HandledAtPropertyName) == null);

    /// <summary>
    /// 按 Id 集合一次投影处理状态（列表页/详情防 N+1）。列投影是处理状态的唯一读口径：
    /// 模块仓储 GetListAsync 为 AsNoTracking，影子列不触发 ABP 的 track 回填，
    /// 实体 ExtraProperties 字典恒为空，GetProperty 读不到（详见接口注释）。
    /// </summary>
    public async Task<List<AuditLogHandledState>> GetStatesAsync(IReadOnlyCollection<Guid> auditLogIds)
    {
        if (auditLogIds.Count == 0)
        {
            return new List<AuditLogHandledState>();
        }

        // AsNoTracking 纯读；租户过滤沿用 AbpAuditLogs 的 IMultiTenant 全局筛选，
        // 与列表查询可见性一致
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.Set<AuditLog>().AsNoTracking()
            .Where(x => auditLogIds.Contains(x.Id))
            .Select(x => new AuditLogHandledState
            {
                AuditLogId = x.Id,
                HandledAt = EF.Property<DateTime?>(x, AuditLogHandleConsts.HandledAtPropertyName),
                HandledByUserId = EF.Property<Guid?>(x, AuditLogHandleConsts.HandledByUserIdPropertyName),
                HandledByName = EF.Property<string>(x, AuditLogHandleConsts.HandledByNamePropertyName),
                HandledNote = EF.Property<string>(x, AuditLogHandleConsts.HandledNotePropertyName)
            })
            .ToListAsync();
    }
}
