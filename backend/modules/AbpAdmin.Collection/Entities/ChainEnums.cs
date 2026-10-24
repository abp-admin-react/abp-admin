namespace AbpAdmin.Collection.Entities;

/// <summary>进款单状态（计划第 5.6 节）：跳过两类是终态；Sweeping/Swept 由归集驱动。</summary>
public enum DepositOrderStatus
{
    /// <summary>扫到、待确认数达标。</summary>
    PendingConfirm = 0,

    /// <summary>确认数已够、待归集（进入归集任务后走 Sweeping/Swept）。</summary>
    Confirmed = 1,

    /// <summary>低于 MinDeposit：终态，不归集不加余额，游标照常前进。</summary>
    SkippedBelowMinDeposit = 2,

    /// <summary>放不进 numeric(28,18)：终态，RawAmount 留原文，Amount 空，告警。</summary>
    PrecisionOverflow = 3,

    /// <summary>已并入归集任务、广播在途。</summary>
    Sweeping = 4,

    /// <summary>归集确认并已入账。</summary>
    Swept = 5,
}

/// <summary>归集任务状态（计划第 5.6 节）：Queued→(Fueling)→Sweeping→Swept；Failed 区分可再试/仅人工。</summary>
public enum SweepTaskStatus
{
    Queued = 0,

    /// <summary>给用户地址打 BNB 做 gas。</summary>
    Fueling = 1,

    /// <summary>代币转账已广播（或正要广播）。</summary>
    Sweeping = 2,

    Swept = 3,

    Failed = 4,
}

/// <summary>
/// 提现单状态（提现 M2，对齐 exchange 的状态机、去掉其废弃的 Approved）：
/// Pending → Processing → Broadcasting → Completed（打款成功终态）
/// Pending → Cancelled（用户撤单，解冻）
/// Pending → Rejected（审核拒绝，解冻）
/// Broadcasting → BroadcastFailed（广播/回执失败）
/// BroadcastFailed → Processing（管理端重试，回执验证通过或双花门通过）
/// BroadcastFailed → Rejected（退款终态：净额+手续费退回可用）
/// 所有迁移都是 CAS 条件 UPDATE（WHERE Status = 期望值），0 行 = InvalidStatus。
/// </summary>
public enum WithdrawStatus
{
    /// <summary>已提交、已冻结净额+手续费，待审核（或小额自动审）。</summary>
    Pending = 0,

    /// <summary>审核通过、冻结已出账，待广播。</summary>
    Processing = 1,

    /// <summary>已签名广播，等链上回执。</summary>
    Broadcasting = 2,

    /// <summary>打款确认完成（链上已付，账本 payout 已过）。</summary>
    Completed = 3,

    /// <summary>拒绝终态（Pending 拒绝=解冻；BroadcastFailed 拒绝=退款）。</summary>
    Rejected = 4,

    /// <summary>广播失败/回执失败，等重试或退款终态。</summary>
    BroadcastFailed = 5,

    /// <summary>用户撤单终态（仅 Pending 可撤，解冻）。</summary>
    Cancelled = 6,
}
