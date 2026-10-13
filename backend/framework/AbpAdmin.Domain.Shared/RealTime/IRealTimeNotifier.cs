using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.RealTime;

/// <summary>
/// 实时推送门面（T3.2）。业务代码（Application / Domain 层）只依赖此接口，
/// 不直接碰 SignalR 的 IHubContext——后者只存在于 HttpApi.Host 层，
/// 且客户端方法名集中在 <see cref="RealTimeMessageNames"/>，改名不用全局搜。
/// 推送是最佳努力交付：实现必须吞掉异常，绝不能影响主流程。
/// SignalR 是加速通道，不是唯一通道；通知本体必须先落库，推送只是"提前告知"。
/// </summary>
public interface IRealTimeNotifier
{
    Task NotifyUserAsync(Guid userId, RealTimeMessage message, CancellationToken cancellationToken = default);

    Task NotifyUsersAsync(IEnumerable<Guid> userIds, RealTimeMessage message, CancellationToken cancellationToken = default);

    /// <summary>推给某租户全体在线连接；tenantId 为 null 表示 Host 侧用户。</summary>
    Task NotifyTenantAsync(Guid? tenantId, RealTimeMessage message, CancellationToken cancellationToken = default);
}

public class RealTimeMessage
{
    /// <summary>客户端方法名，取 <see cref="RealTimeMessageNames"/> 中的常量。</summary>
    public string Name { get; set; } = default!;

    public object? Payload { get; set; }
}

public static class RealTimeMessageNames
{
    public const string Notification = "ReceiveNotification";
    public const string UnreadCount = "ReceiveUnreadCount";
    public const string JobProgress = "ReceiveJobProgress";
    public const string JobCompleted = "ReceiveJobCompleted";
    public const string ExportCompleted = "ReceiveExportCompleted";
}
