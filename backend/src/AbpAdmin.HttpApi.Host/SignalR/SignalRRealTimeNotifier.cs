using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.SignalR;

/// <summary>
/// <see cref="IRealTimeNotifier"/> 的 SignalR 实现（T3.2），覆盖 Domain.Shared 里的
/// <see cref="NullRealTimeNotifier"/>（与 ABP 的 NullEmailSender 同替换模式）。
/// 推送失败只记日志不抛出：SignalR 是加速通道，通知本体已落库，前端下次拉取或轮询会拿到。
/// SignalR:Enabled = false 时全部 no-op（等价于 Null 实现），用于"出问题一键关掉"
/// 而不必回滚部署；该键每次调用读取，改动配置即生效。
/// 用户到连接的映射用 ABP 自带的 AbpSignalRUserIdProvider（IUserIdProvider →
/// CurrentUser.Id），多实例送达由 Redis backplane 负责。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IRealTimeNotifier))]
public class SignalRRealTimeNotifier : IRealTimeNotifier, ITransientDependency
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IConfiguration _configuration;

    public ILogger<SignalRRealTimeNotifier> Logger { get; set; } = NullLogger<SignalRRealTimeNotifier>.Instance;

    public SignalRRealTimeNotifier(
        IHubContext<NotificationHub> hubContext,
        IConfiguration configuration)
    {
        _hubContext = hubContext;
        _configuration = configuration;
    }

    public virtual async Task NotifyUserAsync(Guid userId, RealTimeMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            return;
        }

        try
        {
            await _hubContext.Clients
                .User(userId.ToString())
                .SendAsync(message.Name, message.Payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // 实时推送是最佳努力交付。数据已经落库，前端下次拉取或轮询会拿到。
            Logger.LogWarning(ex, "Real-time push failed. User={UserId} Message={Message}", userId, message.Name);
        }
    }

    public virtual async Task NotifyUsersAsync(IEnumerable<Guid> userIds, RealTimeMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            return;
        }

        try
        {
            await _hubContext.Clients
                .Users(userIds.Select(id => id.ToString()).ToList())
                .SendAsync(message.Name, message.Payload, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Real-time push failed. Message={Message}", message.Name);
        }
    }

    public virtual async Task NotifyTenantAsync(Guid? tenantId, RealTimeMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            return;
        }

        try
        {
            await _hubContext.Clients
                .Group(SignalRGroups.Tenant(tenantId))
                .SendAsync(message.Name, message.Payload, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Real-time push failed. Tenant={TenantId} Message={Message}", tenantId, message.Name);
        }
    }

    private bool IsEnabled()
    {
        return _configuration.GetValue("SignalR:Enabled", true);
    }
}
