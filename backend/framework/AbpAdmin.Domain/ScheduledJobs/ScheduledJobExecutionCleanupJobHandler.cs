using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Linq;
using Volo.Abp.Timing;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 执行历史保留策略（JobType = AbpAdmin.ScheduledJobExecutionCleanup）。
/// 删除 AppScheduledJobExecutions 的过期记录——包括本作业自己产生的记录
/// （按 CreationTime 一刀切，不看 ScheduledJobId，否则保留策略作业本身会成为表膨胀源头）。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class ScheduledJobExecutionCleanupJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = "AbpAdmin.ScheduledJobExecutionCleanup";

    /// <summary>执行历史保留天数。</summary>
    public const int RetentionDays = 30;

    /// <summary>分批删除的批大小：历史积压多时一条 DELETE 会锁表。</summary>
    public const int DeleteBatchSize = 1000;

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:ScheduledJobExecutionCleanup";

    private readonly IRepository<ScheduledJobExecution, Guid> _executionRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IClock _clock;
    private readonly IDataFilter _dataFilter;

    public ScheduledJobExecutionCleanupJobHandler(
        IRepository<ScheduledJobExecution, Guid> executionRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IClock clock,
        IDataFilter dataFilter)
    {
        _executionRepository = executionRepository;
        _asyncExecuter = asyncExecuter;
        _clock = clock;
        _dataFilter = dataFilter;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        // cutoff 与实体 CreationTime 统一走 IClock（默认时钟＝主机本地时）：CreationTime 由
        // ScheduledJobScheduler.RecordAsync 以同一 IClock 的 now 写入（非 UTC 主机上不再偏移一个时区差）
        var cutoff = _clock.Now.AddDays(-RetentionDays);

        // host 级副本在宿主上下文执行（CurrentTenant=null），多租户过滤不过关就只扫到
        // TenantId IS NULL 行——每个租户级作业运行都会写租户属主的执行历史行，宿主副本永不
        // 回收，保留期契约对租户层失效。与 LogRetentionCleaner 同款：关过滤器全租户清扫
        // （纯 DB 删除，无 blob/租户路径耦合）。
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // 按批删除，避免历史积压多时一条 DELETE 锁表。
            // 取消发生在批边界（当前批已提交或未开始），及时响应停机/触发取消。
            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var queryable = await _executionRepository.GetQueryableAsync();
                var expired = await _asyncExecuter.ToListAsync(
                    queryable
                        .Where(x => x.CreationTime < cutoff)
                        .OrderBy(x => x.CreationTime)
                        .Take(DeleteBatchSize),
                    context.CancellationToken);
                if (expired.Count == 0)
                {
                    break;
                }

                // autoSave:true 批内落库（SaveChanges 后实体 Detach，下一轮查询不再返回已删行）；
                // 若为 false，跟踪查询因身份解析反复取回同一批行，积压 >DeleteBatchSize 时死循环
                await _executionRepository.DeleteManyAsync(
                    expired.Select(x => x.Id),
                    autoSave: true,
                    cancellationToken: context.CancellationToken);

                if (expired.Count < DeleteBatchSize)
                {
                    break;
                }
            }
        }
    }
}
