using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.SignalR;

namespace AbpAdmin.SignalR;

/// <summary>
/// 收付款模块实时推送 Hub（链网进度/进款单/归集任务/金库核对/热钱包异常）。
/// 与 NotificationHub 同纪律：只做服务端→客户端单向推送，不写业务方法；
/// 客户端写操作走 HTTP API（UoW/审计/权限拦截器完整生效）。
/// Host 组准入：拒绝租户连接（与 Col* 表无租户列的 AssertHost 同款纪律——
/// 组内广播含全平台资金流水/热钱包异常情报，租户侧用户不得订阅）。
/// </summary>
[Authorize]
[HubRoute("/signalr-hubs/chain-status")]
public class ChainStatusHub : AbpHub
{
    public override async Task OnConnectedAsync()
    {
        // 租户侧用户直连本 Hub 即拒绝（close）：Host 侧分组只收宿主用户
        if (CurrentTenant.Id != null)
        {
            Logger.LogWarning("ChainStatusHub 拒绝租户连接（资金推送仅 Host）。ConnectionId={ConnectionId}",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        // Host 侧分组（收付款模块仅 Host 用户）
        await Groups.AddToGroupAsync(Context.ConnectionId, ChainHubGroups.Host);
        await base.OnConnectedAsync();
        Logger.LogDebug("ChainStatusHub connected. ConnectionId={ConnectionId} UserId={UserId}",
            Context.ConnectionId, CurrentUser.Id);
    }
}

/// <summary>ChainStatusHub 组名与推送事件名约定（Hub 与推送方共用同一出处）。</summary>
public static class ChainHubGroups
{
    /// <summary>Host 侧组（收付款管理页面仅 Host 用户可达）。</summary>
    public const string Host = "chain-status:host";

    /// <summary>扫链进度推送（每轮扫链末尾）。</summary>
    public const string ScanProgressEvent = "ChainScanProgress";

    /// <summary>进款单新增/状态变更推送。</summary>
    public const string DepositChangedEvent = "ChainDepositChanged";

    /// <summary>归集任务状态变更推送。</summary>
    public const string SweepChangedEvent = "ChainSweepChanged";

    /// <summary>金库核对新留痕推送。</summary>
    public const string VaultCheckEvent = "ChainVaultCheck";

    /// <summary>热钱包监控流水新增/异常推送。</summary>
    public const string HotWalletChangedEvent = "ChainHotWalletChanged";
}
