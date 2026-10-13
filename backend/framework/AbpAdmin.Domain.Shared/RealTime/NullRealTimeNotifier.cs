using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.RealTime;

/// <summary>
/// <see cref="IRealTimeNotifier"/> 的空实现，让 Application / Domain 层单测、
/// AbpAdmin.DbMigrator 以及没引 SignalR 的宿主都能正常解析该接口。
/// 与 ABP 自己的 NullEmailSender 同模式：宿主引了 SignalR 后由
/// SignalRRealTimeNotifier（[Dependency(ReplaceServices = true)]）覆盖本实现。
/// </summary>
[ExposeServices(typeof(IRealTimeNotifier))]
public class NullRealTimeNotifier : IRealTimeNotifier, ITransientDependency
{
    public virtual Task NotifyUserAsync(Guid userId, RealTimeMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task NotifyUsersAsync(IEnumerable<Guid> userIds, RealTimeMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task NotifyTenantAsync(Guid? tenantId, RealTimeMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
