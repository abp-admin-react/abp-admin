using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 用户充值地址（计划第 5.6 节）：唯一 (UserId, NetworkId)，唯一 (NetworkId, AddressIndex)。
/// 路径 m/44'/60'/0'/0/i，索引从 2 起（0 不用，1 留给 Gas 地址）。私钥不入库——
/// 归集时按 MnemonicCipher + AddressIndex 现推。派生在 Redis 锁内，撞索引则重读。
/// </summary>
public class ColDepositAddress : BasicAggregateRoot<Guid>
{
    public ColDepositAddress(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColDepositAddress()
    {
    }
#pragma warning restore CS8618

    public Guid UserId { get; set; }

    public Guid NetworkId { get; set; }

    /// <summary>EVM 地址（0x + 40 hex，校验和大小写按 EIP-55 存）。</summary>
    public string Address { get; set; } = default!;

    /// <summary>HD 派生索引（≥2）。</summary>
    public int AddressIndex { get; set; }

    public DateTime CreationTime { get; set; }
}
