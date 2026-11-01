namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>
/// 科目编码。框架种子（FrameworkLedgerSeedProvider）只种通用科目；
/// 标「业务模块」的编码由接入 Ledger 的业务模块自行种，框架新库没有。
/// </summary>
public static class FinanceAccountCodes
{
    // —— 框架通用 ——
    public const string ChannelClearing = "1002";
    public const string AccountsReceivable = "1122";
    public const string AccountsPayable = "2241";
    public const string Unearned = "2205";
    public const string OpeningEquity = "3001";
    public const string ManualAdjust = "3101";
    public const string RetainedEarnings = "3104";
    public const string ServiceIncome = "6002";
    public const string MainCost = "6401";
    public const string GeneralExpense = "6602";

    // —— 业务模块（框架不种）——
    public const string OnChainAsset = "1001";
    public const string UserAvailable = "2201";
    public const string UserFrozen = "2202";
    public const string AgentPayable = "2203";
    public const string RentIncome = "6001";
    public const string CommissionExpense = "6601";

    /// <summary>提现手续费收入（Collection 模块提现 M2 播种）。</summary>
    public const string WithdrawFeeIncome = "6003";
}
