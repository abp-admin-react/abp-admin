using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 代币（计划第 5.6 节表清单）：每币一条，含合约、最低充值额、金库地址、扫链游标。
/// 两个币的游标分开——一种币连续失败不挡住另一种（游标只在本币区间确认读完后前进）。
/// </summary>
public class ColToken : BasicAggregateRoot<Guid>
{
    public ColToken(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColToken()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    /// <summary>USDT / USDC（与 CollectionCurrency 枚举名一致，便于映射余额币种）。</summary>
    public string Symbol { get; set; } = default!;

    /// <summary>ERC20 合约地址（白名单；非白名单合约不建进款单）。</summary>
    public string ContractAddress { get; set; } = default!;

    /// <summary>一期 BSC 上 USDT/USDC 都是 18；换算 RawAmount 时使用。</summary>
    public int Decimals { get; set; }

    /// <summary>低于该额的转入记 SkippedBelowMinDeposit（十进制字符串；用户页展示，按 gas 成本设定）。</summary>
    public string MinDeposit { get; set; } = default!;

    /// <summary>归集目标（金库）地址。系统不保存金库私钥。空 = 该币归集未配置（任务停在 Confirmed）。</summary>
    public string? VaultAddress { get; set; }

    /// <summary>扫链游标：已确认读完的最后一块。null = 从未扫过（首次打开 = 链头 - 确认数）。</summary>
    public long? LastScannedBlock { get; set; }

    /// <summary>该币扫链心跳（扫链心跳超时告警的判据；分币记录便于定位哪条通道停了）。</summary>
    public DateTime? LastScanAt { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreationTime { get; set; }
}
