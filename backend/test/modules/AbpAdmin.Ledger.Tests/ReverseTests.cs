using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>红冲：不删原分录、借贷互换、余额归零、幂等再冲。</summary>
public class ReverseTests : LedgerScenarioTestBase
{
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task Reverse_Creates_Opposite_Entries_And_Zeros_Balances()
    {
        var original = await Ledger.PostAsync(CreditPay("REV-1", 40m, _user));
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(40m);

        var reversalId = await Ledger.ReverseAsync(FinanceBizTypes.DemoCredit, "REV-1", FinanceBizTypes.Phases.Pay);
        reversalId.ShouldNotBe(original);

        var vouchers = await VouchersAsync("REV-1");
        vouchers.Count.ShouldBe(2);
        var orig = vouchers.Single(v => v.Id == original);
        var rev = vouchers.Single(v => v.Id == reversalId);
        orig.Status.ShouldBe(FinVoucherStatus.Reversed);
        rev.ReversalOf.ShouldBe(original);
        rev.BatchKey.ShouldBe(Posting.LedgerPostingEngine.ReversalBatchKeyPrefix + original.ToString("N"));
        rev.Status.ShouldBe(FinVoucherStatus.Posted);

        var origEntries = await EntriesAsync(original);
        var revEntries = await EntriesAsync(reversalId);
        revEntries.Count.ShouldBe(origEntries.Count);
        for (var i = 0; i < origEntries.Count; i++)
        {
            revEntries[i].Debit.ShouldBe(origEntries[i].Credit);
            revEntries[i].Credit.ShouldBe(origEntries[i].Debit);
            revEntries[i].SubAccountId.ShouldBe(origEntries[i].SubAccountId);
        }

        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(0m);
        (await ChannelBalance()).ShouldBe(0m);
        await AssertTrialBalancedAsync();
        await AssertProjectionMatchesEntriesAsync();
    }

    [Fact]
    public async Task Reverse_Replay_Returns_Same_Reversal_Voucher()
    {
        await Ledger.PostAsync(CreditPay("REV-2", 5m, _user));
        var first = await Ledger.ReverseAsync(FinanceBizTypes.DemoCredit, "REV-2", FinanceBizTypes.Phases.Pay);
        var second = await Ledger.ReverseAsync(FinanceBizTypes.DemoCredit, "REV-2", FinanceBizTypes.Phases.Pay);
        second.ShouldBe(first);
        (await VouchersAsync("REV-2")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Reverse_Of_Reversal_Is_Rejected()
    {
        await Ledger.PostAsync(CreditPay("REV-3", 5m, _user));
        var reversal = await Ledger.ReverseAsync(FinanceBizTypes.DemoCredit, "REV-3", FinanceBizTypes.Phases.Pay);
        var rev = (await VouchersAsync("REV-3")).Single(v => v.Id == reversal);
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            Ledger.ReverseAsync(rev.BizType, rev.BizRefId, rev.Phase, rev.BatchKey));
        ex.Code.ShouldBe(FinanceErrorCodes.VoucherNotReversible);
    }

    [Fact]
    public async Task Reverse_Missing_Voucher_Is_404()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            Ledger.ReverseAsync(FinanceBizTypes.DemoCredit, "missing", FinanceBizTypes.Phases.Pay));
        ex.Code.ShouldBe(FinanceErrorCodes.VoucherNotFound);
    }

    [Fact]
    public async Task Reverse_Via_AppService_Updates_Manual_Adjust_Status()
    {
        Guid adjustId;
        using (As(TestUsers.AccountantA))
        {
            var adjust = GetRequiredService<FinManualAdjustAppService>();
            var created = await adjust.CreateAsync(new Services.Dtos.CreateUpdateFinManualAdjustDto
            {
                Amount = 8m, Currency = Cny, DebitAccountCode = FinanceAccountCodes.ChannelClearing,
                DebitOwnerType = FinOwnerType.Channel, DebitOwnerId = "default", Reason = "rev-adj"
            });
            adjustId = created.Id;
            await adjust.SubmitAsync(adjustId);
        }

        using (As(TestUsers.AccountantB))
        {
            var adjust = GetRequiredService<FinManualAdjustAppService>();
            var posted = await adjust.ApproveAsync(adjustId);
            posted.Status.ShouldBe(FinManualAdjustStatus.Posted);

            var voucherSvc = GetRequiredService<FinVoucherAppService>();
            var reversal = await voucherSvc.ReverseAsync(posted.VoucherId!.Value);
            reversal.ReversalOf.ShouldBe(posted.VoucherId);

            var after = await adjust.GetAsync(adjustId);
            after.Status.ShouldBe(FinManualAdjustStatus.Reversed);
            after.ReversalVoucherId.ShouldBe(reversal.Id);
        }
    }
}
