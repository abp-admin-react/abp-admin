using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

public enum VaultReconciliationStatus
{
    Legacy = 0,
    Balanced,
    Surplus,
    TimingDifference,
    Shortfall,
    Inconclusive,
}

/// <summary>
/// 按币保存一次金库核对。历史记录保留原始差额，新记录同时保留地址、确认块和校准差额。
/// </summary>
public class ColVaultCheck : BasicAggregateRoot<Guid>
{
    public ColVaultCheck(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColVaultCheck()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public Guid TokenId { get; set; }

    public string Symbol { get; set; } = default!;

    /// <summary>balanceOf 的原始整数，以代币最小单位计。</summary>
    public string RawVaultAmount { get; set; } = default!;

    /// <summary>换算后的金库余额。</summary>
    public decimal VaultBalance { get; set; }

    /// <summary>本地快照中该币所有用户的可用余额与冻结余额之和。</summary>
    public decimal UserTotal { get; set; }

    /// <summary>金库减当前负债的原始观测值，保留历史口径。</summary>
    public decimal Difference { get; set; }

    public VaultReconciliationStatus ReconciliationStatus { get; set; }
    public long? ReferenceBlock { get; set; }
    public string? ReferenceVaultAddress { get; set; }
    /// <summary>参考块内已到账、在本地快照尚未入账的用户净额。</summary>
    public decimal? PendingCredit { get; set; }
    /// <summary>本地已入账、实际归集在参考块之后的用户净额。</summary>
    public decimal? PostedAfterBlock { get; set; }
    /// <summary>Difference - PendingCredit + PostedAfterBlock。证据不完整及历史数据保留 null。</summary>
    public decimal? ReconciledDifference { get; set; }

    public DateTime CheckedAt { get; set; }
}
