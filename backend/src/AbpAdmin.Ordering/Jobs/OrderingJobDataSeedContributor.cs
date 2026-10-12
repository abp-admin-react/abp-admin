using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ordering.Jobs;
using AbpAdmin.ScheduledJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AbpAdmin.Ordering.Jobs;

/// <summary>
/// 订单模块的作业种子（计划第 2 节）：Host 专用——context.TenantId != null 时直接返回，
/// 只给 Host 插行，默认停用（上线时由人打开，不在种子里打开）。
/// 与框架 ScheduledJobDataSeedContributor 的区别：那个会给 Host 和每个租户各插一行，
/// 且种子直接写仓储、绕过应用服务上的 Host 守卫，所以本模块必须自带只给 Host 的种子。
/// 测试断言租户上下文不会产生这些行（计划第 8 节）。
/// </summary>
public class OrderingJobDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<ScheduledJob, System.Guid> _jobRepository;
    private readonly IGuidGenerator _guidGenerator;

    public OrderingJobDataSeedContributor(
        IRepository<ScheduledJob, System.Guid> jobRepository,
        IGuidGenerator guidGenerator)
    {
        _jobRepository = jobRepository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        // Host 专用作业：租户上下文不产生任何行
        if (context.TenantId != null)
        {
            return;
        }

        var existingNames = (await _jobRepository.GetListAsync())
            .Select(x => x.Name)
            .ToHashSet();

        if (existingNames.Contains(ScheduledJobConsts.OrderStatusSyncJobType))
        {
            return;
        }

        await _jobRepository.InsertAsync(new ScheduledJob(
            _guidGenerator.Create(),
            context.TenantId,
            ScheduledJobConsts.OrderStatusSyncJobType,
            jobType: ScheduledJobConsts.OrderStatusSyncJobType,
            cronExpression: "0 */5 * * * ?",
            description: "订单补状态：到账核对、过期关单、第二笔到账与复活检查（每 5 分钟）",
            isEnabled: false));
    }
}
