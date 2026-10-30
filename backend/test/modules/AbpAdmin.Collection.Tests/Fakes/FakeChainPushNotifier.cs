using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;

namespace AbpAdmin.Collection.Fakes;

/// <summary>
/// 推送假实现（静态单例 <see cref="Instance"/>，DI 注册同一实例）：
/// 收集全部 ETO 供断言（覆盖热钱包监控的「告警+推送」安全载荷）。
/// 测试间复位（同 FakeChannelGateway 的每用例复位纪律，见 CollectionTestBase）。
/// </summary>
public class FakeChainPushNotifier : IChainPushNotifier
{
    /// <summary>静态单例（DI 注册同一实例；测试基类每用例复位）。</summary>
    public static readonly FakeChainPushNotifier Instance = new();

    public List<ChainScanProgressEto> ScanProgress { get; } = new();

    public List<ChainDepositChangedEto> Deposits { get; } = new();

    public List<ChainSweepChangedEto> Sweeps { get; } = new();

    public List<ChainVaultCheckEto> VaultChecks { get; } = new();

    public List<ChainHotWalletChangedEto> HotWallet { get; } = new();

    public static void Reset() => Instance.ResetAll();

    private void ResetAll()
    {
        ScanProgress.Clear();
        Deposits.Clear();
        Sweeps.Clear();
        VaultChecks.Clear();
        HotWallet.Clear();
    }

    public Task PushScanProgressAsync(ChainScanProgressEto progress)
    {
        ScanProgress.Add(progress);
        return Task.CompletedTask;
    }

    public Task PushDepositChangedAsync(ChainDepositChangedEto deposit)
    {
        Deposits.Add(deposit);
        return Task.CompletedTask;
    }

    public Task PushSweepChangedAsync(ChainSweepChangedEto sweep)
    {
        Sweeps.Add(sweep);
        return Task.CompletedTask;
    }

    public Task PushVaultCheckAsync(ChainVaultCheckEto check)
    {
        VaultChecks.Add(check);
        return Task.CompletedTask;
    }

    public Task PushHotWalletChangedAsync(ChainHotWalletChangedEto txn)
    {
        HotWallet.Add(txn);
        return Task.CompletedTask;
    }
}
