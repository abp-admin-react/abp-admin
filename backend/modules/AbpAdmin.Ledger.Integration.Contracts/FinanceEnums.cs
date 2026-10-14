namespace AbpAdmin.Ledger.Integration.Contracts;

public enum FinAccountType
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Income = 3,
    Expense = 4
}

public enum FinNormalSide
{
    Debit = 0,
    Credit = 1
}

public enum FinOwnerType
{
    Platform = 0,
    User = 1,
    Agent = 2,
    Channel = 3
}

public enum FinVoucherStatus
{
    Posted = 0,
    Reversed = 1
}

/// <summary>
/// 调账五态（定稿 §5.1）：Draft→Pending→Rejected/Posted；Rejected→Draft；Posted→Reversed。
/// 数值随 F1 重排（F0 无 Pending、表内无存量）。
/// </summary>
public enum FinManualAdjustStatus
{
    Draft = 0,
    Pending = 1,
    Rejected = 2,
    Posted = 3,
    Reversed = 4
}

public enum FinPeriodStatus
{
    Open = 0,
    Closed = 1
}

public enum FinPostingRuleStatus
{
    Draft = 0,
    Approved = 1,
    Deprecated = 2
}
