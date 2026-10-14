namespace AbpAdmin.Ledger.Settings;

public static class LedgerSettings
{
    /// <summary>设置页 Tab 分组名（本地化键同时挂 SettingUiResource）。</summary>
    public const string SettingUiGroup = "Finance";

    private const string Prefix = "Finance.Ledger";

    public const string Enabled = Prefix + ".Enabled";
    public const string PrepaymentFreezeWrites = "Finance.Prepayment.FreezeWrites";
    public const string WalletUseLedgerProjection = "Finance.Wallet.UseLedgerProjection";
}
