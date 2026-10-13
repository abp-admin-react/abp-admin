using System;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Settings;
using Volo.Abp.Threading;

namespace AbpAdmin.Identity;

/// <summary>
/// T4.1：按设置项清理不活跃会话。默认 1 小时一轮、保留 30 天。
/// 判定用 LastAccessed（没有则 SignedIn）。请求中间件会 TouchIfStaleAsync，避免把还在打 API 的会话当僵尸。
/// </summary>
public class IdentitySessionCleanupBackgroundWorker : AsyncPeriodicBackgroundWorkerBase
{
    public IdentitySessionCleanupBackgroundWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(1).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var settingProvider = workerContext.ServiceProvider.GetRequiredService<ISettingProvider>();
        var days = await settingProvider.GetAsync(AbpAdminSettings.Account.SessionCleanupInactiveDays, 30);
        if (days <= 0)
        {
            return;
        }

        var manager = workerContext.ServiceProvider.GetRequiredService<IdentitySessionManager>();
        await manager.CleanupInactiveAsync(TimeSpan.FromDays(days));
    }
}
