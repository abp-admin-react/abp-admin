using System;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace AbpAdmin.Gdpr;

/// <summary>
/// 过期 GDPR 请求清理。
/// 个人数据请求的导出结果不该长期留存在系统里（数据最小化），到期连请求记录
/// 与各模块贡献的 GdprInfo payload 一起删除。
/// 保留天数走 AbpAdminGdprOptions.RetentionDays（法域相关合规参数）；
/// 时间源用 IClock 与写入侧（GdprRequestAppService 的 Clock.Now）保持一致，避免非 UTC 部署的系统性偏差。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class GdprRequestCleanupJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = "AbpAdmin.GdprRequestCleanup";

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:GdprRequestCleanup";

    private readonly IRepository<GdprRequest, Guid> _requestRepository;
    private readonly IRepository<GdprInfo, Guid> _infoRepository;
    private readonly IClock _clock;
    private readonly AbpAdminGdprOptions _gdprOptions;
    private readonly IDataFilter _dataFilter;

    public GdprRequestCleanupJobHandler(
        IRepository<GdprRequest, Guid> requestRepository,
        IRepository<GdprInfo, Guid> infoRepository,
        IClock clock,
        IOptions<AbpAdminGdprOptions> gdprOptions,
        IDataFilter dataFilter)
    {
        _requestRepository = requestRepository;
        _infoRepository = infoRepository;
        _clock = clock;
        _gdprOptions = gdprOptions.Value;
        _dataFilter = dataFilter;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        var cutoff = _clock.Now.AddDays(-_gdprOptions.RetentionDays);

        // host 级副本宿主上下文执行，过滤器不过关就只删 TenantId IS NULL 行——租户用户
        // 的 GDPR 请求/个人数据行永不过期，保留期承诺（RetentionDays）对租户层失效。
        // 关过滤器全租户清扫（纯 DB 删除）。
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var expiredRequests = await _requestRepository.GetListAsync(x => x.CreationTime < cutoff);

            foreach (var request in expiredRequests)
            {
                await _infoRepository.DeleteAsync(x => x.RequestId == request.Id);
                await _requestRepository.DeleteAsync(request);
            }
        }
    }
}
