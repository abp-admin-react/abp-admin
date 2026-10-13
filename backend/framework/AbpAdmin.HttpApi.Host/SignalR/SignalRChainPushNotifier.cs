using System;
using System.Threading.Tasks;
using AbpAdmin.SignalR;
using Contracts = AbpAdmin.Collection.Contracts;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.SignalR;

/// <summary>
/// 收付款 SignalR 推送实现（宿主侧）。Collection 模块经 Contracts.IChainPushNotifier 接口调用，
/// 本类把 ETO 转发给 ChainStatusHub 的 Host 组。事件名与 ChainHubGroups 保持一处定义。
/// </summary>
public class SignalRChainPushNotifier : Contracts.IChainPushNotifier, ITransientDependency
{
    private readonly IHubContext<ChainStatusHub> _hubContext;

    public SignalRChainPushNotifier(IHubContext<ChainStatusHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PushScanProgressAsync(Contracts.ChainScanProgressEto progress)
    {
        await _hubContext.Clients.Group(ChainHubGroups.Host)
            .SendAsync(ChainHubGroups.ScanProgressEvent, progress);
    }

    public async Task PushDepositChangedAsync(Contracts.ChainDepositChangedEto deposit)
    {
        await _hubContext.Clients.Group(ChainHubGroups.Host)
            .SendAsync(ChainHubGroups.DepositChangedEvent, deposit);
    }

    public async Task PushSweepChangedAsync(Contracts.ChainSweepChangedEto sweep)
    {
        await _hubContext.Clients.Group(ChainHubGroups.Host)
            .SendAsync(ChainHubGroups.SweepChangedEvent, sweep);
    }

    public async Task PushVaultCheckAsync(Contracts.ChainVaultCheckEto check)
    {
        await _hubContext.Clients.Group(ChainHubGroups.Host)
            .SendAsync(ChainHubGroups.VaultCheckEvent, check);
    }
}
