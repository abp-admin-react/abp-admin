using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 链网络（计划第 5.6 节表清单）：一期只有 BSC 一行（种子建）。
/// 助记词密文与 RPC 密文存本表（MnemonicCipher/RpcCipher，列名已登记 SensitiveEntityProperties），
/// 主密钥来自宿主配置 Collection:MasterKey，密文带一字节版本前缀。私钥不入库（扫链/归集时按索引现推）。
/// 心跳与告警冷却时间也放本表（同一类告警对同一主体有冷却）。
/// </summary>
public class ColNetwork : BasicAggregateRoot<Guid>
{
    public ColNetwork(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColNetwork()
    {
    }
#pragma warning restore CS8618

    /// <summary>如 "BSC"。唯一。</summary>
    public string Name { get; set; } = default!;

    /// <summary>EIP-155 链 ID（BSC 主网 56）。广播前核对，防把交易发错网。</summary>
    public long ChainId { get; set; }

    /// <summary>助记词密文（版本前缀 + ABP StringEncryption，口令 = Collection:MasterKey）。null = 未设置。</summary>
    public string? MnemonicCipher { get; set; }

    /// <summary>RPC 连接串密文（URL 含项目密钥）。null = 未设置。</summary>
    public string? RpcCipher { get; set; }

    /// <summary>
    /// 单次 getLogs 响应的条数上限（计划第 5.6 节）：优先节点报错实测，静默截断用服务商文档，
    /// 都没有则拒绝打开扫链（不设缺省）。返回条数达到上限八成视为没读完。
    /// </summary>
    public int? NodeLogResultCap { get; set; }

    /// <summary>扫链游标按币放在 ColToken 上；这里放网络级心跳与链头快照（心跳超时告警的判据）。</summary>
    public DateTime? ScanHeartbeatAt { get; set; }

    public DateTime? SweepHeartbeatAt { get; set; }

    /// <summary>最近一次观测到的链头（扫链落后过深告警的分母）。</summary>
    public long? LatestChainHead { get; set; }

    // ── 告警冷却（同一类告警对同一主体的上次触发时间；恢复时记一条由作业自行判断）──

    public DateTime? GasLowAlertedAt { get; set; }

    public DateTime? ScanBehindAlertedAt { get; set; }

    public DateTime? SweepStuckAlertedAt { get; set; }

    public DateTime CreationTime { get; set; }
}
