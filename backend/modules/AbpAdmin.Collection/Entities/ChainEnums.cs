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
