using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Collection.Services;

/// <summary>钱包与财务的只读核对，以及商业退款回钱包。核对不补账。</summary>
public class WalletLedgerAppService : ApplicationService
{
    private readonly WalletLedgerBaselineService _baseline;
    private readonly CollectionBalanceManager _balances;

    public WalletLedgerAppService(WalletLedgerBaselineService baseline, CollectionBalanceManager balances)
    {
        _baseline = baseline;
        _balances = balances;
    }

    [Authorize(CollectionPermissions.Collections.Default)]
    public virtual Task<WalletLedgerBaselineReport> GetBaselineAsync()
    {
        AssertHost();
        return _baseline.BuildAsync();
    }

    [Authorize(CollectionPermissions.WalletRefunds.Refund)]
    public virtual Task<WalletRefundHoldResult> RefundAsync(WalletRefundInput input)
    {
        AssertHost();
        return _balances.RefundWalletAsync(input.OriginalDebitTxnId, input.RequestId, CollectionAmounts.Parse(input.Amount));
    }

    [Authorize(CollectionPermissions.WalletRefunds.Refund)]
    public virtual Task ConfirmHoldAsync(WalletRefundConfirmInput input)
    {
        AssertHost();
        return _balances.ConfirmWalletRefundHoldAsync(input.OriginalDebitTxnId, input.RequestId);
    }

    /// <summary>余额表没有租户列。权限种子漏配时，租户身份也不能动这笔钱。</summary>
    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}

public class WalletRefundInput
{
    public Guid OriginalDebitTxnId { get; set; }

    public string RequestId { get; set; } = default!;

    public string Amount { get; set; } = default!;
}

public class WalletRefundConfirmInput
{
    public Guid OriginalDebitTxnId { get; set; }

    public string RequestId { get; set; } = default!;
}
