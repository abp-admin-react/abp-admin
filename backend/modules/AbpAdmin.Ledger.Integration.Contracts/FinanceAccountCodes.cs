namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>种子科目编码（定稿 §0/§3）。</summary>
public static class FinanceAccountCodes
{
    public const string OnChainAsset = "1001";
    public const string ChannelClearing = "1002";
    public const string UserAvailable = "2201";
    public const string UserFrozen = "2202";
    public const string AgentPayable = "2203";
    public const string Unearned = "2205";
    public const string OpeningEquity = "3001";
    public const string ManualAdjust = "3101";
    public const string RentIncome = "6001";
    public const string ServiceIncome = "6002";
    public const string CommissionExpense = "6601";
}
