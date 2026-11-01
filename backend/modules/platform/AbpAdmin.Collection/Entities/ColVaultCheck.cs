using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 金库核对留痕（计划第 5.6 节）：每日按币追加一行 balanceOf(金库) vs 用户余额合计。
/// 未入账的在途不加入右边（否则归集完成前会误报）；正差额不告警（负差额 = 金库 < 用户余额，告警）。
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

    /// <summary>balanceOf(金库) 原始整数十进制字符串（18 位小数单位）。</summary>
    public string RawVaultAmount { get; set; } = default!;

    /// <summary>换算后的金库余额。</summary>
    public decimal VaultBalance { get; set; }

    /// <summary>该币用户余额合计（ColBalances）。</summary>
    public decimal UserTotal { get; set; }

    /// <summary>VaultBalance - UserTotal；正差额不告警。</summary>
    public decimal Difference { get; set; }

    public DateTime CheckedAt { get; set; }
}
