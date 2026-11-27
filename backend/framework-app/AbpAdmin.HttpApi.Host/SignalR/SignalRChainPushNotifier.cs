using System.Threading.Tasks;
using AbpAdmin.Collection.Permissions;
using Contracts = AbpAdmin.Collection.Contracts;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.SignalR;

/// <summary>按发送时的有效读取权限，把资金事件转发到已认证用户的连接。</summary>
public class SignalRChainPushNotifier(IHubContext<ChainStatusHub> hub, IChainPushAudience audience)
    : Contracts.IChainPushNotifier, ITransientDependency
{
    private static readonly string[] Operations = [CollectionPermissions.Collections.Default, CollectionPermissions.Network.Admin];
    private static readonly string[] Network = [CollectionPermissions.Network.Admin];
    private static readonly string[] HotWallet = [CollectionPermissions.Withdraw.Admin];

    public Task PushScanProgressAsync(Contracts.ChainScanProgressEto progress) =>
        PublishAsync(Network, ChainHubGroups.ScanProgressEvent, progress);

    public Task PushDepositChangedAsync(Contracts.ChainDepositChangedEto deposit) =>
        PublishAsync(Operations, ChainHubGroups.DepositChangedEvent, deposit);

    public Task PushSweepChangedAsync(Contracts.ChainSweepChangedEto sweep) =>
        PublishAsync(Operations, ChainHubGroups.SweepChangedEvent, sweep);

    public Task PushVaultCheckAsync(Contracts.ChainVaultCheckEto check) =>
        PublishAsync(Operations, ChainHubGroups.VaultCheckEvent, check);

    public Task PushHotWalletChangedAsync(Contracts.ChainHotWalletChangedEto transaction) =>
        PublishAsync(HotWallet, ChainHubGroups.HotWalletChangedEvent, transaction);

    private async Task PublishAsync(string[] permissions, string eventName, object payload)
    {
        var groups = await audience.GetGroupsAsync(permissions);
        if (groups.Count > 0)
            await hub.Clients.Groups(groups).SendAsync(eventName, payload);
    }
}
