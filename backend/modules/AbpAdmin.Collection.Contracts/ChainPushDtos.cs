using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AbpAdmin.Collection.Contracts;

/// <summary>代币通道扫链进度推送载荷（ChainStatusHub:ChainScanProgress）。</summary>
[Serializable]
public class ChainScanProgressEto
{
    public string Symbol { get; set; } = default!;
    public long? LastScannedBlock { get; set; }
    public long? LatestChainHead { get; set; }
    public long? BlocksBehind { get; set; }
    public DateTime? LastScanAt { get; set; }
    public bool IsEnabled { get; set; }
}

/// <summary>进款单变更推送载荷（ChainStatusHub:ChainDepositChanged）。</summary>
[Serializable]
public class ChainDepositChangedEto
{
    public Guid DepositOrderId { get; set; }
    public string TxHash { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    public Guid UserId { get; set; }
    public string Status { get; set; } = default!;
    public string? Amount { get; set; }
    public long BlockNumber { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>归集任务状态变更推送载荷（ChainStatusHub:ChainSweepChanged）。</summary>
[Serializable]
public class ChainSweepChangedEto
{
    public Guid SweepTaskId { get; set; }
    public string Symbol { get; set; } = default!;
    public Guid UserId { get; set; }
    public string Status { get; set; } = default!;
    public string Amount { get; set; } = default!;
    public DateTime StatusChangedAt { get; set; }
}

/// <summary>金库核对留痕推送载荷（ChainStatusHub:ChainVaultCheck）。</summary>
[Serializable]
public class ChainVaultCheckEto
{
    public Guid CheckId { get; set; }
    public string Symbol { get; set; } = default!;
    public string VaultBalance { get; set; } = default!;
    public string UserTotal { get; set; } = default!;
    public string Difference { get; set; } = default!;
    public DateTime CheckedAt { get; set; }
}

/// <summary>热钱包监控流水新增/异常推送载荷（ChainStatusHub:ChainHotWalletChanged）。</summary>
[Serializable]
public class ChainHotWalletChangedEto
{
    public Guid TxnId { get; set; }
    public string Symbol { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string Direction { get; set; } = default!;
    public string TxHash { get; set; } = default!;
    public string Amount { get; set; } = default!;
    public string To { get; set; } = default!;
    public long BlockNumber { get; set; }
}

/// <summary>
/// 收付款实时推送出口（契约侧接口；宿主用 SignalR IHubContext 实现，
/// 测试/迁移进程可用空实现）。Collection 模块的 Worker/Manager 调它，
/// 不直接依赖 SignalR（保持模块自包含）。
/// </summary>
public interface IChainPushNotifier
{
    Task PushScanProgressAsync(ChainScanProgressEto progress);
    Task PushDepositChangedAsync(ChainDepositChangedEto deposit);
    Task PushSweepChangedAsync(ChainSweepChangedEto sweep);
    Task PushVaultCheckAsync(ChainVaultCheckEto check);
    Task PushHotWalletChangedAsync(ChainHotWalletChangedEto txn);
}
