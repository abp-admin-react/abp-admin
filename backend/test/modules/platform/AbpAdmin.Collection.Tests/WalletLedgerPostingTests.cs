using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace AbpAdmin.Collection;

public class WalletLedgerPostingTests : CollectionTestBase
{
    private static readonly Guid User1 = Guid.Parse("12121212-1212-1212-1212-121212121212");

    private CollectionBalanceManager Balances => GetRequiredService<CollectionBalanceManager>();

    private CollectionLedgerPoster Poster => GetRequiredService<CollectionLedgerPoster>();

    private async Task EnforceAsync()
    {
        await GetRequiredService<ISettingManager>()
            .SetGlobalAsync(CollectionSettings.WalletLedger.Enforce, "true");
    }

    private async Task FundAsync(decimal amount)
    {
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, amount, $"dep-{Guid.NewGuid():N}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, amount);
    }

    private async Task<decimal> WalletAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var row = await db.Balances.AsNoTracking().SingleAsync(b => b.UserId == User1);
        await uow.CompleteAsync();
        return row.Balance;
    }

    [Fact]
    public async Task Baseline_Reports_Wallet_Above_And_Below_The_Ledger()
    {
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 50m, $"dep-{Guid.NewGuid():N}");
        var high = await GetRequiredService<WalletLedgerBaselineService>().BuildAsync();
        high.IsConsistent.ShouldBeFalse();
        high.Rows.ShouldContain(row => row.UserId == User1 && row.WalletAvailable == 50m && row.LedgerAvailable == 0m);
        high.SingleWriters.ShouldNotBeEmpty();

        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 40m);
        var low = await GetRequiredService<WalletLedgerBaselineService>().BuildAsync();
        low.Rows.ShouldContain(row => row.UserId == User1 && row.WalletAvailable == 50m && row.LedgerAvailable == 40m);
    }

    [Fact]
    public async Task Mismatched_User_Is_Not_Debited()
    {
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 50m, $"dep-{Guid.NewGuid():N}");
        await EnforceAsync();

        var error = await Should.ThrowAsync<BusinessException>(() =>
            Balances.DebitAsync("order", Guid.NewGuid(), User1, CollectionCurrency.Usdt, 10m));

        error.Code.ShouldBe(CollectionErrorCodes.WalletLedgerPending);
        (await WalletAsync()).ShouldBe(50m);
    }

    [Fact]
    public async Task Debit_Posts_With_The_Balance_And_Rolls_Back_When_The_Ledger_Is_Off()
    {
        await FundAsync(100m);
        await EnforceAsync();
        var businessId = Guid.NewGuid();
        var txnId = await Balances.DebitAsync("order", businessId, User1, CollectionCurrency.Usdt, 10m);

        (await WalletAsync()).ShouldBe(90m);
        (await Poster.GetUserAvailableBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(90m);
        (await Poster.GetOrderPrepayBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(10m);
        (await Poster.HasOrderDebitVoucherAsync(txnId)).ShouldBeTrue();

        await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.Enabled, "false");
        await Should.ThrowAsync<BusinessException>(() =>
            Balances.DebitAsync("order", Guid.NewGuid(), User1, CollectionCurrency.Usdt, 5m));
        (await WalletAsync()).ShouldBe(90m);
        (await Poster.GetUserAvailableBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(90m);
    }

    [Fact]
    public async Task Reverse_Without_A_Voucher_Leaves_The_Wallet_Untouched()
    {
        await FundAsync(100m);
        var businessId = Guid.NewGuid();
        await Balances.DebitAsync("order", businessId, User1, CollectionCurrency.Usdt, 10m);
        await EnforceAsync();

        var pending = await Should.ThrowAsync<BusinessException>(() => Balances.ReverseAsync("order", businessId));

        pending.Code.ShouldBe(CollectionErrorCodes.WalletLedgerPending);
        (await WalletAsync()).ShouldBe(90m);
    }

    [Fact]
    public async Task Reverse_Restores_The_Wallet_And_The_Order_Bucket()
    {
        await FundAsync(100m);
        await EnforceAsync();
        var businessId = Guid.NewGuid();
        var txnId = await Balances.DebitAsync("order", businessId, User1, CollectionCurrency.Usdt, 10m);

        await Balances.ReverseAsync("order", businessId);

        (await WalletAsync()).ShouldBe(100m);
        (await Poster.GetUserAvailableBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(100m);
        (await Poster.GetOrderPrepayBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(0m);
        (await Poster.HasOrderDebitVoucherAsync(txnId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Reverse_And_Refund_Cannot_Both_Credit()
    {
        await FundAsync(100m);
        await EnforceAsync();
        var businessId = Guid.NewGuid();
        var txnId = await Balances.DebitAsync("order", businessId, User1, CollectionCurrency.Usdt, 100m);

        Exception? reverseError = null;
        Exception? refundError = null;
        await Task.WhenAll(
            Task.Run(async () =>
            {
                try
                {
                    using var scope = ServiceProvider.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>()
                        .ReverseAsync("order", businessId);
                }
                catch (Exception ex)
                {
                    reverseError = ex;
                }
            }),
            Task.Run(async () =>
            {
                try
                {
                    using var scope = ServiceProvider.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>()
                        .RefundWalletAsync(txnId, "race", 100m);
                }
                catch (Exception ex)
                {
                    refundError = ex;
                }
            }));

        (reverseError == null && refundError == null).ShouldBeFalse();
        (await WalletAsync()).ShouldBe(100m);
        (await Poster.GetUserAvailableBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(100m);
        (await Poster.GetOrderPrepayBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(0m);
    }

    [Fact]
    public async Task Linked_Note_Does_Not_Occupy_The_Refund_Again()
    {
        await FundAsync(100m);
        await EnforceAsync();
        var businessId = Guid.NewGuid();
        var txnId = await Balances.DebitAsync("order", businessId, User1, CollectionCurrency.Usdt, 100m);
        await Balances.RefundWalletAsync(txnId, "paid-40", 40m);
        (await WalletAsync()).ShouldBe(40m);

        await Balances.LinkWalletRefundNoteAsync(txnId, "paid-40", 40m);
        await Balances.LinkWalletRefundNoteAsync(txnId, "paid-40");
        (await WalletAsync()).ShouldBe(40m);
        (await RefundOccupiedAsync(txnId)).ShouldBe(40m);

        (await Should.ThrowAsync<BusinessException>(() => Balances.LinkWalletRefundNoteAsync(txnId, "paid-40", 39m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundConflict);
        (await Should.ThrowAsync<BusinessException>(() => Balances.LinkWalletRefundNoteAsync(txnId, "missing")))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundNotAllowed);

        var hold = await Balances.HoldWalletRefundAsync(txnId, "pending", 10m);
        (await Should.ThrowAsync<BusinessException>(() => Balances.LinkWalletRefundNoteAsync(txnId, "pending", 10m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundNotAllowed);
        (await Should.ThrowAsync<BusinessException>(() => Balances.RefundWalletAsync(txnId, "while-pending", 50m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundNotAllowed);
        await Balances.ConfirmWalletRefundHoldAsync(txnId, "pending");
        (await WalletAsync()).ShouldBe(40m);
        hold.RefundId.ShouldNotBeNull();

        (await Balances.RefundWalletAsync(txnId, "rest", 50m)).Applied.ShouldBeTrue();
        (await Should.ThrowAsync<BusinessException>(() => Balances.RefundWalletAsync(txnId, "over", 1m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundExceeds);
        (await WalletAsync()).ShouldBe(90m);
        (await RefundOccupiedAsync(txnId)).ShouldBe(100m);
        (await Poster.GetOrderPrepayBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(10m);
    }

    [Fact]
    public async Task Linked_Note_And_Refund_Do_Not_Double_Count()
    {
        await FundAsync(100m);
        await EnforceAsync();
        var txnId = await Balances.DebitAsync("order", Guid.NewGuid(), User1, CollectionCurrency.Usdt, 100m);
        await Balances.RefundWalletAsync(txnId, "paid-40", 40m);

        await Task.WhenAll(
            Task.Run(async () =>
            {
                using var scope = ServiceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>()
                    .LinkWalletRefundNoteAsync(txnId, "paid-40", 40m);
            }),
            Task.Run(async () =>
            {
                using var scope = ServiceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>()
                    .RefundWalletAsync(txnId, "rest-60", 60m);
            }));

        (await WalletAsync()).ShouldBe(100m);
        (await RefundOccupiedAsync(txnId)).ShouldBe(100m);
        (await RefundRowCountAsync(txnId)).ShouldBe(2);
    }

    private async Task<decimal> RefundOccupiedAsync(Guid debitId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var sum = await db.WalletRefunds.Where(row => row.OriginalDebitTxnId == debitId).SumAsync(row => row.Amount);
        await uow.CompleteAsync();
        return sum;
    }

    private async Task<int> RefundRowCountAsync(Guid debitId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var count = await db.WalletRefunds.CountAsync(row => row.OriginalDebitTxnId == debitId);
        await uow.CompleteAsync();
        return count;
    }

    [Fact]
    public async Task Refund_Is_Idempotent_Per_Debit_And_Cannot_Exceed_Or_Clear_The_Debit()
    {
        await FundAsync(100m);
        await EnforceAsync();
        var firstOrder = Guid.NewGuid();
        var first = await Balances.DebitAsync("order", firstOrder, User1, CollectionCurrency.Usdt, 100m);
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 20m, $"top-{Guid.NewGuid():N}");
        await Poster.PostDepositCreditAsync(Guid.NewGuid(), User1, CollectionCurrency.Usdt, 20m);
        var secondOrder = Guid.NewGuid();
        var second = await Balances.DebitAsync("order", secondOrder, User1, CollectionCurrency.Usdt, 20m);

        var refund = await Balances.RefundWalletAsync(first, "same", 60m);
        var replay = await Balances.RefundWalletAsync(first, "same", 60m);
        replay.RefundId.ShouldBe(refund.RefundId);
        (await Should.ThrowAsync<BusinessException>(() => Balances.RefundWalletAsync(first, "same", 40m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundConflict);
        (await Balances.RefundWalletAsync(second, "same", 20m)).Applied.ShouldBeTrue();

        (await Should.ThrowAsync<BusinessException>(() => Balances.RefundWalletAsync(first, "other", 60m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundExceeds);
        (await WalletAsync()).ShouldBe(80m);

        var hold = await Balances.HoldWalletRefundAsync(first, "hold", 40m);
        (await Should.ThrowAsync<BusinessException>(() => Balances.RefundWalletAsync(first, "after-hold", 1m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundNotAllowed);
        var beforeConfirm = await WalletAsync();
        await Balances.ConfirmWalletRefundHoldAsync(first, "hold");
        (await WalletAsync()).ShouldBe(beforeConfirm);
        (await Should.ThrowAsync<BusinessException>(() => Balances.RefundWalletAsync(first, "after-confirm", 1m)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundExceeds);
        (await Should.ThrowAsync<BusinessException>(() => Balances.ReverseAsync("order", firstOrder)))
            .Code.ShouldBe(CollectionErrorCodes.WalletRefundNotAllowed);

        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var debit = await db.BalanceTxns.SingleAsync(txn => txn.Id == first);
        debit.ReversedAt.ShouldBeNull();
        (await db.WalletRefunds.CountAsync(row => row.OriginalDebitTxnId == first && row.Status == WalletRefundStatus.Succeeded))
            .ShouldBe(1);
        hold.RefundId.ShouldNotBeNull();
        await uow.CompleteAsync();
    }

    [Fact]
    public async Task Controlled_Vouchers_And_Balance_Accounts_Cannot_Be_Adjusted()
    {
        await FundAsync(30m);
        await EnforceAsync();
        var txnId = await Balances.DebitAsync("order", Guid.NewGuid(), User1, CollectionCurrency.Usdt, 10m);
        var ledger = GetRequiredService<ILedgerIntegrationService>();
        var debitVoucher = await ledger.HasVoucherAsync(
            FinanceBizTypes.OrderWallet, txnId.ToString("D"), FinanceBizTypes.Phases.OrderDebit);
        debitVoucher.ShouldBeTrue();

        var vouchers = GetRequiredService<FinVoucherAppService>();
        Guid voucherId;
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var db = await GetRequiredService<IDbContextProvider<AbpAdmin.Ledger.Data.LedgerDbContext>>().GetDbContextAsync();
            voucherId = await db.Vouchers.Where(v => v.BizRefId == txnId.ToString("D")).Select(v => v.Id).SingleAsync();
            await uow.CompleteAsync();
        }

        (await Should.ThrowAsync<BusinessException>(() => vouchers.ReverseAsync(voucherId)))
            .Code.ShouldBe(FinanceErrorCodes.VoucherNotReversible);
        (await Poster.GetUserAvailableBalanceAsync(User1, CollectionCurrency.Usdt)).ShouldBe(20m);

        var ordinary = await ledger.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.ManualAdjust, Guid.NewGuid().ToString("N"), FinanceBizTypes.Phases.Post,
            "USDT", 1m,
            From: new AccountRef(FinanceAccountCodes.OnChainAsset),
            To: new AccountRef(FinanceAccountCodes.WithdrawFeeIncome),
            Summary: "ordinary"));
        await vouchers.ReverseAsync(ordinary);

        var me = GetRequiredService<ICurrentUser>().Id!.Value;
        Guid adjustId;
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(isTransactional: true))
        {
            var rules = GetRequiredService<PostingRuleProvider>();
            var available = await rules.GetAccountAsync(FinanceAccountCodes.UserAvailable);
            var manual = await rules.GetAccountAsync(FinanceAccountCodes.ManualAdjust);
            var db = await GetRequiredService<IDbContextProvider<AbpAdmin.Ledger.Data.LedgerDbContext>>().GetDbContextAsync();
            var adjust = new FinManualAdjust(Guid.NewGuid(), me, new FinManualAdjustLines(
                1m, "USDT", available.Id, FinOwnerType.User, User1.ToString(),
                manual.Id, FinOwnerType.Platform, ""), "touch wallet", null);
            adjust.Submit(DateTime.UtcNow);
            await db.ManualAdjusts.AddAsync(adjust);
            await db.SaveChangesAsync();
            adjustId = adjust.Id;
            await uow.CompleteAsync();
        }

        (await Should.ThrowAsync<BusinessException>(() =>
                GetRequiredService<FinManualAdjustAppService>().ApproveAsync(adjustId)))
            .Code.ShouldBe(FinanceErrorCodes.ControlledWalletAccount);
    }
}
