using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Security.Claims;

namespace AbpAdmin.SignalR;

/// <summary>
/// 接纳具有资金读取权限的宿主用户，将连接加入该用户及其认证客户端对应的组。
/// 每次推送按事件需要的读取权限选择接收组。
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
        if (CurrentTenant.Id != null || CurrentUser.TenantId != null || !CurrentUser.IsAuthenticated || CurrentUser.Id is not Guid userId)
        {
            Logger.LogWarning("ChainStatusHub 拒绝连接，资金推送要求已认证的宿主用户身份。ConnectionId={ConnectionId}",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        var grants = await _permissionChecker.IsGrantedAsync(
            [CollectionPermissions.Collections.Default, CollectionPermissions.Network.Admin, CollectionPermissions.Withdraw.Admin]);
        if (!grants.Result.Values.Contains(PermissionGrantResult.Granted))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ChainHubGroups.ForRecipient(userId, null));
        if (Context.User?.FindFirst(AbpClaimTypes.ClientId)?.Value is { Length: > 0 } client)
            await Groups.AddToGroupAsync(Context.ConnectionId, ChainHubGroups.ForRecipient(userId, client));
        await base.OnConnectedAsync();
        Logger.LogDebug("ChainStatusHub connected. ConnectionId={ConnectionId} UserId={UserId}",
            Context.ConnectionId, CurrentUser.Id);
    }
}

/// <summary>ChainStatusHub 组名与推送事件名约定（Hub 与推送方共用同一出处）。</summary>
public static class ChainHubGroups
{
    internal static string ForRecipient(Guid userId, string? client) => client == null
        ? $"chain-status:user:{userId:D}"
        : $"chain-status:user:{userId:D}:client:{client}";

    /// <summary>扫链进度推送（每轮扫链末尾）。</summary>
    public const string ScanProgressEvent = "ChainScanProgress";

    /// <summary>进款单新增/状态变更推送。</summary>
    public const string DepositChangedEvent = "ChainDepositChanged";

    /// <summary>归集任务状态变更推送。</summary>
    public const string SweepChangedEvent = "ChainSweepChanged";

    /// <summary>金库核对新留痕推送。</summary>
    public const string VaultCheckEvent = "ChainVaultCheck";

    /// <summary>热钱包监控发现新的未匹配出账。</summary>
    public const string HotWalletChangedEvent = "ChainHotWalletChanged";
}
