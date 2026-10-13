using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Jobs;

/// <summary>
/// 收款模块的作业种子（流水合计核对，计划第 5.5 节）。与 OrderingJobDataSeedContributor
/// 同一纪律：Host 专用（租户上下文不插行）、默认停用——上线由人打开。
/// </summary>
public class CollectionJobDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<ScheduledJob, System.Guid> _jobRepository;
    private readonly IGuidGenerator _guidGenerator;

    public CollectionJobDataSeedContributor(
        IRepository<ScheduledJob, System.Guid> jobRepository,
        IGuidGenerator guidGenerator)
    {
        _jobRepository = jobRepository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId != null)
        {
            return;
        }

        var existing = (await _jobRepository.GetListAsync())
            .Select(x => x.JobType)
            .ToHashSet();

        if (!existing.Contains(ScheduledJobConsts.BalanceAuditJobType))
        {
            await _jobRepository.InsertAsync(new ScheduledJob(
                _guidGenerator.Create(),
                context.TenantId,
                ScheduledJobConsts.BalanceAuditJobType,
                jobType: ScheduledJobConsts.BalanceAuditJobType,
                cronExpression: "0 20 3 * * ?",
                description: "余额流水合计核对：按用户和币种核对流水之和等于余额行，不等则告警（每日）",
                isEnabled: false));
        }
    }
}
