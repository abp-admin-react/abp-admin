using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

/// <summary>
/// 热钱包流水方向：Out = 热钱包转出（应付提现，未匹配即异常）；In = 转入热钱包（外部补币，提示性）。
/// </summary>
public enum HotWalletDirection
{
    Out = 0,
    In = 1,
}

/// <summary>
/// 热钱包流水匹配状态。
/// </summary>
public enum HotWalletTxnStatus
{
    /// <summary>出站且匹配到提现单（TxHash 对上）——正常付币。</summary>
    Matched = 0,

    /// <summary>入站转入热钱包——外部补币/运营充值，无对应业务单是预期，提示性展示。</summary>
    ExternalIn = 1,

    /// <summary>出站但匹配不到任何提现单——未解释出账，高危（私钥泄漏/误操作），告警并等确认。</summary>
    Anomaly = 2,

    /// <summary>异常已被管理员确认（已知操作），终态。</summary>
    Acked = 3,
}

/// <summary>
/// 热钱包逐笔监控流水（按币扫热钱包地址的 ERC20 Transfer，Etherscan tokentx 通道）：
/// 出站逐笔对账提现单 TxHash，未匹配标异常告警；入站记为外部补币。
/// 唯一 (TxHash, LogIndex)——重启/重扫幂等。一期不含余额核对点重放（基线快照留二期）。
/// </summary>
public class ColHotWalletTxn : BasicAggregateRoot<Guid>
{
    public ColHotWalletTxn(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColHotWalletTxn()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public Guid TokenId { get; set; }

    public string Symbol { get; set; } = default!;

    public string TxHash { get; set; } = default!;

    public int LogIndex { get; set; }

    public string From { get; set; } = default!;

    public string To { get; set; } = default!;

    public HotWalletDirection Direction { get; set; }

    /// <summary>链上原始整数十进制串（精度溢出时的保底展示）。</summary>
    public string RawAmount { get; set; } = default!;

    /// <summary>缩放后金额（null = 超精度，展示回退 RawAmount）。</summary>
    public decimal? Amount { get; set; }

    public long BlockNumber { get; set; }

    public HotWalletTxnStatus Status { get; set; }

    /// <summary>出站匹配到的提现单（ColWithdrawOrders.Id）；入站/未匹配为 null。</summary>
    public Guid? MatchedWithdrawId { get; set; }

    public string? AckedBy { get; set; }

    public DateTime? AckedAt { get; set; }

    public string? AckNote { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 热钱包监控游标（按币一行）：LastScannedBlock 随每个完成的小窗即时持久化推进
///（预算取消/重启不丢已扫进度——大积压场景的活锁防线）；首跑基线 = 当时安全链头
///（历史无基线可完整回扫，余额核对重放留二期）。区间读不全时缩半分窗自愈；
/// 单块仍读不全（超分页上限）按块跳过推进——LastError 聚合留痕跳块范围 + [Alert:] 告警，
/// 绝不静默卡死游标；流水表 (TxHash,LogIndex) 唯一键吸收一切重扫。
/// </summary>
public class ColHotWalletWatchState : BasicAggregateRoot<Guid>
{
    public ColHotWalletWatchState(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected ColHotWalletWatchState()
    {
    }
#pragma warning restore CS8618

    public Guid NetworkId { get; set; }

    public Guid TokenId { get; set; }

    public string Symbol { get; set; } = default!;

    /// <summary>已扫到的安全链头（head − 确认数）；null = 尚未首跑。</summary>
    public long? LastScannedBlock { get; set; }

    public DateTime? LastScanAt { get; set; }

    /// <summary>最近一轮失败原因（成功后清空）。</summary>
    public string? LastError { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
