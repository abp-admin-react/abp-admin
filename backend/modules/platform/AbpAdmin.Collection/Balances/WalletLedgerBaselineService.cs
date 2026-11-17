using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Balances;

/// <summary>
/// 当前态核对。同一个事务读钱包、流水重算和财务投影，只出差异，不补分录。
/// </summary>
public class WalletLedgerBaselineService : ITransientDependency
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly CollectionLedgerPoster _ledgerPoster;

    public WalletLedgerBaselineService(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IUnitOfWorkManager unitOfWorkManager,
        CollectionLedgerPoster ledgerPoster)
    {
        _dbContextProvider = dbContextProvider;
        _unitOfWorkManager = unitOfWorkManager;
        _ledgerPoster = ledgerPoster;
    }

    public virtual async Task<WalletLedgerBaselineReport> BuildAsync()
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ");

        var balances = await dbContext.Balances.AsNoTracking().ToListAsync();
        var txns = await dbContext.BalanceTxns.AsNoTracking().ToListAsync();
        var refunds = await dbContext.WalletRefunds.AsNoTracking().ToListAsync();
        var refundedByDebit = refunds
            .Where(refund => refund.Status == WalletRefundStatus.Succeeded)
            .GroupBy(refund => refund.OriginalDebitTxnId)
            .ToDictionary(group => group.Key, group => group.Sum(refund => refund.Amount));
        var ledger = await _ledgerPoster.ListUserLiabilityBalancesAsync();
        var withdrawals = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(order => order.Status == WithdrawStatus.Processing
                            || order.Status == WithdrawStatus.Broadcasting
                            || order.Status == WithdrawStatus.BroadcastFailed)
            .Select(order => new WalletInFlightRow
            {
                WithdrawalId = order.Id,
                UserId = order.UserId,
                Currency = order.Currency.ToString(),
                Status = order.Status.ToString(),
                NetAmount = CollectionAmounts.Format(order.ActualAmount),
            })
            .ToListAsync();

        var rows = new Dictionary<string, WalletLedgerBaselineRow>();
        WalletLedgerBaselineRow Row(Guid userId, CollectionCurrency currency)
        {
            var key = userId.ToString("N") + ":" + currency;
            if (!rows.TryGetValue(key, out var row))
            {
                row = new WalletLedgerBaselineRow
                {
                    UserId = userId,
                    Currency = currency.ToString(),
                };
                rows[key] = row;
            }

            return row;
        }

        foreach (var balance in balances)
        {
            var row = Row(balance.UserId, balance.Currency);
            row.WalletAvailable = balance.Balance;
            row.WalletFrozen = balance.Frozen;
            row.HasWallet = true;
        }

        foreach (var group in txns.GroupBy(txn => (txn.UserId, txn.Currency)))
        {
            var row = Row(group.Key.UserId, group.Key.Currency);
            row.TxnAvailable = group.Where(txn => txn.Action != BalanceActions.WithdrawSettle).Sum(txn => txn.Amount);
            row.TxnFrozen = group.Sum(txn => txn.Action switch
            {
                BalanceActions.WithdrawSettle => txn.Amount,
                BalanceActions.WithdrawFreeze or BalanceActions.WithdrawUnfreeze => -txn.Amount,
                _ => 0m,
            });
        }

        foreach (var line in ledger)
        {
            if (!Guid.TryParse(line.OwnerId, out var userId))
            {
                continue;
            }

            if (!TryCurrency(line.Currency, out var currency))
            {
                continue;
            }

            var row = Row(userId, currency);
            if (line.AccountCode == FinanceAccountCodes.UserAvailable)
            {
                row.LedgerAvailable = line.Balance;
            }
            else if (line.AccountCode == FinanceAccountCodes.UserFrozen)
            {
                row.LedgerFrozen = line.Balance;
            }
            else if (line.AccountCode == FinanceAccountCodes.Unearned
                     && line.Bucket == FinanceAccountCodes.OrderPrepayBucket)
            {
                row.LedgerOrderPrepay = line.Balance;
            }
        }

        foreach (var group in txns.Where(txn => txn.Action == BalanceActions.BusinessDebit && txn.ReversedAt == null)
                     .GroupBy(txn => (txn.UserId, txn.Currency)))
        {
            var refunded = group.Sum(txn => refundedByDebit.GetValueOrDefault(txn.Id));
            var row = Row(group.Key.UserId, group.Key.Currency);
            row.ExpectedOrderPrepay = group.Sum(txn => -txn.Amount) - refunded;
        }

        var unreversedDebitIds = txns
            .Where(txn => txn.Action == BalanceActions.BusinessDebit && txn.ReversedAt == null)
            .Select(txn => txn.Id.ToString("D"))
            .ToList();
        var postedRefs = await _ledgerPoster.FindOrderDebitVoucherRefsAsync(unreversedDebitIds);
        var unvouchered = unreversedDebitIds
            .Where(id => !postedRefs.Contains(id))
            .Select(id => Guid.Parse(id))
            .ToList();

        await uow.CompleteAsync();
        var mismatched = rows.Values
            .Where(row => row.HasWallet && (row.WalletAvailable != row.LedgerAvailable || row.WalletFrozen != row.LedgerFrozen)
                          || row.ExpectedOrderPrepay != row.LedgerOrderPrepay
                          || row.HasWallet && (row.WalletAvailable != row.TxnAvailable || row.WalletFrozen != row.TxnFrozen))
            .OrderBy(row => row.UserId)
            .ThenBy(row => row.Currency)
            .ToList();
        return new WalletLedgerBaselineReport
        {
            IsConsistent = mismatched.Count == 0,
            Rows = mismatched,
            UnvoucheredDebitIds = unvouchered,
            InFlightWithdrawals = withdrawals,
            SingleWriters =
            [
                "Collection.WalletLedger.Enforce 为 false 时，余额扣款和技术回冲只写 ColBalances。",
                "OrderStatusSyncJobHandler 对用户取消的未回冲扣款调用 ReverseDebitAsync；没有原凭证时记异常后停止重试。",
                "开账回填只补正差，不作为历史修复。",
                "人工退款记录在双写启用前只追加说明和标价币种累计，不写总账。",
            ],
        };
    }

    private static bool TryCurrency(string code, out CollectionCurrency currency)
    {
        if (string.Equals(code, "USDT", StringComparison.OrdinalIgnoreCase))
        {
            currency = CollectionCurrency.Usdt;
            return true;
        }

        if (string.Equals(code, "USDC", StringComparison.OrdinalIgnoreCase))
        {
            currency = CollectionCurrency.Usdc;
            return true;
        }

        currency = default;
        return false;
    }
}

public class WalletLedgerBaselineReport
{
    public bool IsConsistent { get; set; }

    public List<WalletLedgerBaselineRow> Rows { get; set; } = new();

    public List<Guid> UnvoucheredDebitIds { get; set; } = new();

    public List<WalletInFlightRow> InFlightWithdrawals { get; set; } = new();

    public List<string> SingleWriters { get; set; } = new();
}

public class WalletLedgerBaselineRow
{
    public Guid UserId { get; set; }

    public string Currency { get; set; } = default!;

    public bool HasWallet { get; set; }

    public decimal WalletAvailable { get; set; }

    public decimal WalletFrozen { get; set; }

    public decimal TxnAvailable { get; set; }

    public decimal TxnFrozen { get; set; }

    public decimal LedgerAvailable { get; set; }

    public decimal LedgerFrozen { get; set; }

    public decimal ExpectedOrderPrepay { get; set; }

    public decimal LedgerOrderPrepay { get; set; }
}

public class WalletInFlightRow
{
    public Guid WithdrawalId { get; set; }

    public Guid UserId { get; set; }

    public string Currency { get; set; } = default!;

    public string Status { get; set; } = default!;

    public string NetAmount { get; set; } = default!;
}
