using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.SignalR;

namespace AbpAdmin.SignalR;

/// <summary>
/// 站内通知/作业进度/导出完成的实时推送 Hub（T3.2）。
/// 只做服务端 → 客户端单向推送，不写业务方法：客户端调业务走 HTTP API，
/// 走 Hub 会绕开 ABP 的 UoW、审计、权限拦截器（Hub filter 只做认证）。
/// 路由显式声明（前端硬编码该路径），不依赖"类名去 Hub 后缀转 kebab-case"的约定。
/// Hub 由 AbpSignalRConventionalRegistrar 自动注册进 DI 并自动映射路由，无需手写 MapHub。
/// </summary>
[Authorize]
[HubRoute("/signalr-hubs/notification")]
public class NotificationHub : AbpHub
{
    public override async Task OnConnectedAsync()
    {
        // 租户分组：SignalRRealTimeNotifier.NotifyTenantAsync 按 $"tenant:{tenantId}" 组推送。
        // 连接断开时 SignalR 自动把连接移出所有组，无需 OnDisconnectedAsync 里手动移除。
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.Tenant(CurrentTenant.Id));

        await base.OnConnectedAsync();

        Logger.LogDebug(
            "SignalR connected. ConnectionId={ConnectionId} UserId={UserId} TenantId={TenantId}",
            Context.ConnectionId, CurrentUser.Id, CurrentTenant.Id);
    }
}

/// <summary>SignalR 组名约定，Hub 加组与 Notifier 推送必须走同一个函数。</summary>
internal static class SignalRGroups
{
    /// <summary>tenantId 为 null 表示 Host 侧用户组。</summary>
    internal static string Tenant(Guid? tenantId) => $"tenant:{tenantId}";
}
