using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 链上 Gas 成本实耗台账（按确认回执的 gasUsed × effectiveGasPrice 结算记录，按 NetworkId + TxHash 幂等）。
/// </summary>
public class ColGasFeeLog : BasicAggregateRoot<Guid>
{
    public ColGasFeeLog(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColGasFeeLog()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public ChainTxPurpose Purpose { get; set; }

    public Guid? RefId { get; set; }

    public string TxHash { get; set; } = default!;

    public string SenderAddress { get; set; } = default!;

    public long? BlockNumber { get; set; }

    public long GasUsed { get; set; }

    public string EffectiveGasPriceWei { get; set; } = default!;

    public decimal FeeBnb { get; set; }

    public DateTime CreationTime { get; set; }
}
