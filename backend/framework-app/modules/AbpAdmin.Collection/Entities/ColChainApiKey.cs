using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>扫链 API KEY 的服务商通道：一把 key 一行，同服务商可挂多把（轮换抗限流）。</summary>
public enum ChainApiKeyProvider
{
    /// <summary>Ankr BSC JSON-RPC 项目密钥（拼 https://rpc.ankr.com/bsc/{key} 扫链/广播）。</summary>
    RpcBsc = 1,

    /// <summary>Etherscan V2 多链 API Key（按地址兜底查 tokentx，免费档限流最紧）。</summary>
    EtherscanV2 = 2,
}

/// <summary>
/// 扫链 API KEY（同服务商多把，管理页增删启停）：
/// - 明文只传不存（KeyCipher 版本前缀 + ABP StringEncryption，口令 = Collection:MasterKey）；
/// - KeyHash = SHA-256(明文) hex，同 (NetworkId, Provider, KeyHash) 唯一——密文带随机 IV
///   不能做唯一判重，哈希列才是真「同把 key 不重复入库」；
/// - 轮换判据：工厂每次组网关按 IsEnabled + SortOrder + CreationTime 取全量，
///   网关内遇 429/401/403/配额类错误自动切下一把；LastUsedAt 只是运维可见度，不参与排序。
/// </summary>
public class ColChainApiKey : BasicAggregateRoot<Guid>
{
    public ColChainApiKey(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColChainApiKey()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public ChainApiKeyProvider Provider { get; set; }

    /// <summary>运维备注名（如 "ankr-main" / "ankr-backup2"）。可空。</summary>
    public string? Label { get; set; }

    /// <summary>SHA-256(key) 小写 hex（判重与唯一索引；明文不可回推）。</summary>
    public string KeyHash { get; set; } = default!;

    /// <summary>key 密文（版本前缀 + ABP StringEncryption）。审计黑名单属性（KeyCipher）。</summary>
    public string KeyCipher { get; set; } = default!;

    public bool IsEnabled { get; set; }

    /// <summary>工厂最近一次把该 key 排进网关的时间（运维可见度）。</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>最近一轮被网关标记限流/认证失败的 key 数（展示用；轮换决策在网关内存里做）。</summary>
    public int ConsecutiveFailures { get; set; }

    public DateTime CreationTime { get; set; }
}
