using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp;

namespace AbpAdmin.Ledger.Posting;

/// <summary>
/// 用户可用、冻结和订单预收桶只能由钱包命令改。空桶 2205 和平台 2241 仍走原财务权限。
/// </summary>
public static class WalletLedgerGuard
{
    public static bool IsControlledBizType(string bizType)
    {
        return bizType is FinanceBizTypes.Deposit
            or FinanceBizTypes.Withdraw
            or FinanceBizTypes.Wallet
            or FinanceBizTypes.WalletAdjustment
            or FinanceBizTypes.OrderWallet;
    }

    public static bool TouchesControlled(string accountCode, string? bucket)
    {
        return accountCode is FinanceAccountCodes.UserAvailable or FinanceAccountCodes.UserFrozen
            || (accountCode == FinanceAccountCodes.Unearned
                && string.Equals(bucket, FinanceAccountCodes.OrderPrepayBucket, System.StringComparison.Ordinal));
    }

    public static void RejectManualAccount(string accountCode, string? bucket = null)
    {
        if (!TouchesControlled(accountCode, bucket))
        {
            return;
        }

        throw new BusinessException(FinanceErrorCodes.ControlledWalletAccount)
            .WithData("AccountCode", accountCode)
            .WithData("Bucket", bucket ?? "");
    }
}
