using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AbpAdmin.Logging;

/// <summary>
/// 日志保留期清理后台 Worker：每 24 小时一轮，按 <see cref="LogRetentionOptions"/> 批删过期日志。
/// 注册在 Host（宿主级基础设施，DbMigrator 不感知——与 IdentitySessionCleanupBackgroundWorker 同位）。
/// 两个 RetentionDays 都缺省/0 时直接空转，配置即开关。
/// </summary>
public class LogRetentionBackgroundWorker : AsyncPeriodicBackgroundWorkerBase
{
    public LogRetentionBackgroundWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(24).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var options = workerContext.ServiceProvider
            .GetRequiredService<IOptions<LogRetentionOptions>>().Value;
        if (options.OperationLogRetentionDays <= 0 && options.AuditLogRetentionDays <= 0)
        {
            return;
        }

        var removed = await workerContext.ServiceProvider
            .GetRequiredService<LogRetentionCleaner>()
            .CleanAsync(options, workerContext.CancellationToken);

        if (removed > 0)
        {
            Logger.LogInformation("日志保留期清理完成，共删除 {Removed} 行（操作日志/审计日志）", removed);
        }
    }
}
