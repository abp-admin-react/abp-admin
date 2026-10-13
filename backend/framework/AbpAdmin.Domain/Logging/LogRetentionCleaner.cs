using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Logging;

/// <summary>
/// 日志保留期清理器：批删过期的操作日志（AppOperationLogs）与审计日志（AbpAuditLogs）。
/// 分批策略：按 Id 升序每次取最旧一批、当批内删除、循环至空——<c>DeleteAsync(谓词)</c> 会把
/// 命中行整表物化（百万行级日志表不可行），且分批 + 每批独立 UoW（requiresNew）避免单长事务
/// 持锁。批内「查询即删除」保证游标必然推进，循环必然终止。
/// 全租户清理：两张表都是 IMultiTenant，ABP 查询过滤器在宿主侧（CurrentTenant 为空）只放行
/// TenantId IS NULL——必须经 IDataFilter 关闭多租户过滤器，租户名下的过期日志才会被清到
/// （与 ScheduledJobScheduler/ImpersonationManager 宿主侧跨租户作业同一惯例）。
/// 批大小钳制到非负：负值会让 Take(n) 变成非法 LIMIT 直接抛错，而文档语义是「≤0 空转不清理」。
/// 稳态成本 O(删除行数)：过期行集中在 Id 索引最旧端，每批从上轮终点续扫、随删随进；
/// 例外是「零命中轮」（截止时间早于全部数据，如新启用保留期）会沿 Id 索引整表走一遍才返回空
/// ——只读、无锁、每晚至多一次，大表上会出现在 pg_stat_statements，属接受的行为。
/// 审计子表（AbpAuditLogActions / AbpEntityChanges）由库级 FK Cascade 随主行删除。
/// 截止时间用 IClock（本地/UTC 混写会造成「刚写入即过期」或「永不过期」）。
/// </summary>
public class LogRetentionCleaner : ITransientDependency
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IRepository<OperationLog, Guid> _operationLogRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IClock _clock;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;

    public LogRetentionCleaner(
        IUnitOfWorkManager unitOfWorkManager,
        IRepository<OperationLog, Guid> operationLogRepository,
        IAuditLogRepository auditLogRepository,
        IClock clock,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _operationLogRepository = operationLogRepository;
        _auditLogRepository = auditLogRepository;
        _clock = clock;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
    }

    /// <summary>清理两张日志表，返回删除总行数。RetentionDays 缺省/0 的表直接跳过。</summary>
    public async Task<long> CleanAsync(LogRetentionOptions options, CancellationToken cancellationToken = default)
    {
        return await CleanOperationLogsAsync(options, cancellationToken)
             + await CleanAuditLogsAsync(options, cancellationToken);
    }

    /// <summary>清理过期操作日志，返回删除行数。</summary>
    public async Task<long> CleanOperationLogsAsync(LogRetentionOptions options, CancellationToken cancellationToken = default)
    {
        var cutoff = CutoffOrNull(options.OperationLogRetentionDays);
        return cutoff == null
            ? 0
            : await DeleteExpiredBatchesAsync(
                cutoff.Value, options.BatchSize,
                async () => (await _operationLogRepository.GetQueryableAsync())
                    .Where(x => x.ExecutionTime < cutoff.Value)
                    .OrderBy(x => x.Id)
                    .Take(BoundBatchSize(options)),
                async batch => await _operationLogRepository.DeleteManyAsync(batch, autoSave: false, cancellationToken: cancellationToken),
                cancellationToken);
    }

    /// <summary>清理过期审计日志（子表库级联），返回删除行数。</summary>
    public async Task<long> CleanAuditLogsAsync(LogRetentionOptions options, CancellationToken cancellationToken = default)
    {
        var cutoff = CutoffOrNull(options.AuditLogRetentionDays);
        return cutoff == null
            ? 0
            : await DeleteExpiredBatchesAsync(
                cutoff.Value, options.BatchSize,
                async () => (await _auditLogRepository.GetQueryableAsync())
                    .Where(x => x.ExecutionTime < cutoff.Value)
                    .OrderBy(x => x.Id)
                    .Take(BoundBatchSize(options)),
                async batch => await _auditLogRepository.DeleteManyAsync(batch, autoSave: false, cancellationToken: cancellationToken),
                cancellationToken);
    }

    /// <summary>负 BatchSize 会让 Take(n) 翻译成非法 LIMIT 抛错；语义是「≤0 空转」，钳到 0 即空转。</summary>
    private static int BoundBatchSize(LogRetentionOptions options)
        => Math.Max(0, options.BatchSize);

    private DateTime? CutoffOrNull(int retentionDays)
        => retentionDays > 0 ? _clock.Now.AddDays(-retentionDays) : null;

    private async Task<long> DeleteExpiredBatchesAsync<TEntity>(
        DateTime cutoff,
        int batchSize,
        Func<Task<IQueryable<TEntity>>> nextBatch,
        Func<List<TEntity>, Task> deleteBatch,
        CancellationToken cancellationToken)
        where TEntity : Entity<Guid>
    {
        long removed = 0;
        // 宿主侧跨租户清理：两张表都是 IMultiTenant，过滤器在宿主侧只放行 TenantId IS NULL，
        // 不关掉则租户名下的过期日志永远清不到（磁盘无限增长——本 Worker 存在的意义落空）
        using (_dataFilter.Disable<IMultiTenant>())
        {
            while (true)
            {
                List<TEntity> batch;
                using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
                {
                    batch = await _asyncExecuter.ToListAsync(await nextBatch(), cancellationToken);
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    await deleteBatch(batch);
                    await uow.CompleteAsync(cancellationToken);
                }

                removed += batch.Count;
                if (batch.Count < batchSize)
                {
                    break;
                }
            }
        }

        return removed;
    }
}
