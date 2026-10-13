using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Files;

/// <summary>
/// 缩略图回填（T3.3 第 6 步，JobType = AbpAdmin.ThumbnailBackfill）。
/// 业务逻辑在 T3.1 交付的 <see cref="FileThumbnailBackfillManager"/>，这里只是调度挂接：
/// 扫 Pending 映射行与缺失映射行的图片文件，批量入队一次性缩略图作业。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class ThumbnailBackfillJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = "AbpAdmin.ThumbnailBackfill";

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:ThumbnailBackfill";

    private readonly FileThumbnailBackfillManager _backfillManager;

    public ThumbnailBackfillJobHandler(FileThumbnailBackfillManager backfillManager)
    {
        _backfillManager = backfillManager;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        // UoW 由调度器（ScheduledJobScheduler.RunAsync）开好；管理器内部自己关多租户过滤做全量扫描
        await _backfillManager.EnqueuePendingAndMissingAsync(context.CancellationToken);
    }
}
