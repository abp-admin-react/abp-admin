using System.Threading.Tasks;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Authorization.Permissions;

namespace AbpAdmin.SignalR;

/// <summary>
/// 收付款模块实时推送 Hub（链网进度/进款单/归集任务/金库核对/热钱包异常）。
/// 与 NotificationHub 同纪律：只做服务端→客户端单向推送，不写业务方法；
/// 客户端写操作走 HTTP API（UoW/审计/权限拦截器完整生效）。
/// 拒绝租户和无读取权限的连接；按各类 HTTP 查询已有权限加入对应的推送组。
/// 组内广播含资金流水和钱包情报，仅登录宿主不构成读取授权。
/// </summary>
[Authorize]
[HubRoute("/signalr-hubs/chain-status")]
public class ChainStatusHub : AbpHub
{
    private readonly IPermissionChecker _permissionChecker;

    public ChainStatusHub(IPermissionChecker permissionChecker)
    {
        _permissionChecker = permissionChecker;
    }

    public override async Task OnConnectedAsync()
    {
        // 与 HTTP API 一样仅接纳已登录的宿主用户；认证中间件仍保留 [Authorize]。
        if (CurrentTenant.Id != null || !CurrentUser.IsAuthenticated)
        {
            Logger.LogWarning("ChainStatusHub 拒绝匿名或租户连接（资金推送仅 Host）。ConnectionId={ConnectionId}",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        var grants = await _permissionChecker.IsGrantedAsync(
            [CollectionPermissions.Collections.Default, CollectionPermissions.Network.Admin, CollectionPermissions.Withdraw.Admin]);
        bool Granted(string permission) => grants.Result.TryGetValue(permission, out var grant)
            && grant == PermissionGrantResult.Granted;
        var canReadOperations = Granted(CollectionPermissions.Collections.Default)
            || Granted(CollectionPermissions.Network.Admin);
        var canReadScanProgress = Granted(CollectionPermissions.Network.Admin);
        var canReadHotWallet = Granted(CollectionPermissions.Withdraw.Admin);
        if (!canReadOperations && !canReadScanProgress && !canReadHotWallet)
        {
            Context.Abort();
            return;
        }

        if (canReadOperations)
            await Groups.AddToGroupAsync(Context.ConnectionId, ChainHubGroups.Operations);
        if (canReadScanProgress)
            await Groups.AddToGroupAsync(Context.ConnectionId, ChainHubGroups.ScanProgress);
        if (canReadHotWallet)
            await Groups.AddToGroupAsync(Context.ConnectionId, ChainHubGroups.HotWallet);
        await base.OnConnectedAsync();
        Logger.LogDebug("ChainStatusHub connected. ConnectionId={ConnectionId} UserId={UserId}",
            Context.ConnectionId, CurrentUser.Id);
    }
}

/// <summary>ChainStatusHub 组名与推送事件名约定（Hub 与推送方共用同一出处）。</summary>
public static class ChainHubGroups
{
    /// <summary>充值/归集/储备查询：Collections 或 Network.Admin，与 CheckOperationsReadAccessAsync 相同。</summary>
    public const string Operations = "chain-status:operations";

    /// <summary>扫链运行状态：Network.Admin，与 CollectionNetworkAdminAppService.GetStatusAsync 相同。</summary>
    public const string ScanProgress = "chain-status:scan-progress";

    /// <summary>提现钱包流水：WithdrawAdmin，与 CollectionWithdrawAdminAppService 相同。</summary>
    public const string HotWallet = "chain-status:hot-wallet";

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
