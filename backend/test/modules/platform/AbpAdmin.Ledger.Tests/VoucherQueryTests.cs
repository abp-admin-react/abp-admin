using AbpAdmin.Grids;
using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Ledger;

public class VoucherQueryTests : LedgerScenarioTestBase
{
    [Fact]
    public async Task Grid_Filters_Reconcile_Snapshot_Dates_And_Sorts_Difference_Counts()
    {
        using (As(TestUsers.AccountantA))
        {
            var service = GetRequiredService<FinReconcileAppService>();
            var start = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);
            var first = await service.RunAsync(new FinReconcileRunInput { RangeStart = start, RangeEnd = start.AddDays(1) });
            await service.RunAsync(new FinReconcileRunInput { RangeStart = start.AddDays(1), RangeEnd = start.AddDays(2) });
            var page = await service.QueryListAsync(new GridQueryInput
            {
                Filters = [new() { Field = "rangeStart", Conditions = [new() { Op = "eq", Value = "2026-01-07" }] }],
                Sort = [new() { Field = "diffCount", Direction = "desc" }],
            }, new FinReconcileListInput());
            page.TotalCount.ShouldBe(1);
            page.Items[0].Id.ShouldBe(first.Id);
        }
    }

    [Fact]
    public async Task Grid_Matches_All_Pages_Before_Sorting_Decimals_And_Aggregates()
    {
        var user = Guid.NewGuid();
        await Ledger.PostAsync(CreditPay("grid-older", 2m, user));
        await Ledger.PostAsync(CreditPay("grid-newer", 10.000000000000000001m, user));
        var service = GetRequiredService<FinVoucherAppService>();
        var query = new GridQueryInput
        {
            Keyword = "GRID-", SkipCount = 1, MaxResultCount = 1,
            Filters = [new() { Field = "entryCount", Conditions = [new() { Op = "eq", Value = "2" }] }],
            Sort = [new() { Field = "amountText", Direction = "desc" }],
        };
        var page = await service.QueryListAsync(query, new() { UserId = user });
        page.TotalCount.ShouldBe(2);
        page.Items[0].AmountText.ShouldBe("2");
        var balances = await GetRequiredService<FinLedgerAppService>().QueryBalanceProjectionsAsync(
            new GridQueryInput { Keyword = user.ToString(), Sort = [new() { Field = "ledgerDerived", Direction = "desc" }] });
        balances.TotalCount.ShouldBe(1);
        balances.Items[0].OwnerId.ShouldBe(user.ToString());
    }

    [Fact]
    public async Task Transaction_Query_Filters_By_User_Email_And_Voucher_And_Preserves_Amounts()
    {
        var userId = Guid.NewGuid();
        var email = $"ledger-{userId:N}@example.test";
        await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<IdentityUser, Guid>>()
            .InsertAsync(new IdentityUser(userId, $"ledger-{userId:N}", email), autoSave: true));
        const decimal amount = 100.000000000000000001m;
        var voucherId = await Ledger.PostAsync(CreditPay($"query-{userId:N}", amount, userId));
        var service = GetRequiredService<FinVoucherAppService>();

        var result = await service.GetListAsync(new FinVoucherListInput
        {
            UserId = userId,
            VoucherId = voucherId,
            Email = email.ToUpperInvariant(),
        });
        result.TotalCount.ShouldBe(1);
        result.Items[0].UserId.ShouldBe(userId);
        result.Items[0].EntryCount.ShouldBe(2);
        result.Items[0].AmountText.ShouldBe("100.000000000000000001");
        result.Items[0].TotalDebitText.ShouldBe(result.Items[0].TotalCreditText);

        (await service.GetListAsync(new FinVoucherListInput { VoucherId = voucherId, UserId = Guid.NewGuid() }))
            .TotalCount.ShouldBe(0);
        (await service.GetListAsync(new FinVoucherListInput { Email = "missing@example.test" }))
            .TotalCount.ShouldBe(0);

        var detail = await service.GetAsync(voucherId);
        detail.EntryCount.ShouldBe(2);
        detail.Entries.ShouldNotBeNull();
        detail.Entries.ShouldAllBe(entry => entry.BalanceAfterText != null);
        detail.Entries.ShouldContain(entry => entry.CreditText == "100.000000000000000001");
    }

    [Fact]
    public async Task Email_Filter_Respects_Current_Tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var email = $"shared-{Guid.NewGuid():N}@example.test";
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        foreach (var pair in new[] { (Tenant: tenantA, User: userA), (Tenant: tenantB, User: userB) })
        {
            using var tenant = GetRequiredService<ICurrentTenant>().Change(pair.Tenant);
            await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<IdentityUser, Guid>>()
                .InsertAsync(new IdentityUser(pair.User, $"ledger-{pair.User:N}", email, pair.Tenant), autoSave: true));
            await Ledger.PostAsync(CreditPay($"query-{pair.User:N}", 3m, pair.User));
        }

        using (GetRequiredService<ICurrentTenant>().Change(tenantA))
        {
            var result = await GetRequiredService<FinVoucherAppService>().GetListAsync(new FinVoucherListInput { Email = email });
            result.TotalCount.ShouldBe(1);
            result.Items[0].UserId.ShouldBe(userA);
        }
    }

    [Fact]
    public async Task Wallet_Adjustment_Voucher_Cannot_Be_Reversed_Through_Accounting_Only_Endpoint()
    {
        var id = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
            db.Vouchers.Add(new FinVoucher(id, FinanceBizTypes.WalletAdjustment, id.ToString(), "post", "USDT", 1m));
            await db.SaveChangesAsync();
        });

        var exception = await Should.ThrowAsync<BusinessException>(() => GetRequiredService<FinVoucherAppService>().ReverseAsync(id));
        exception.Code.ShouldBe(FinanceErrorCodes.VoucherNotReversible);
        (await ReadAsync(db => db.Vouchers.SingleAsync(voucher => voucher.Id == id))).Status.ShouldBe(FinVoucherStatus.Posted);
    }
}
