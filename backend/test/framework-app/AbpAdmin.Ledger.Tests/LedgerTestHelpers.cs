using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Ledger;

public abstract class LedgerScenarioTestBase : LedgerTestBase
{
    protected const string Cny = "CNY";

    protected ILedgerIntegrationService Ledger => GetRequiredService<ILedgerIntegrationService>();

    protected Task<decimal> UserBalance(string code, Guid user) =>
        Ledger.GetBalanceAsync(new AccountRef(code, FinOwnerType.User, user.ToString()), Cny);

    protected Task<decimal> AgentBalance(Guid agent) =>
        Ledger.GetBalanceAsync(new AccountRef(FinanceAccountCodes.AgentPayable, FinOwnerType.Agent, agent.ToString()), Cny);

    protected Task<decimal> PlatformBalance(string code) =>
        Ledger.GetBalanceAsync(new AccountRef(code, FinOwnerType.Platform, ""), Cny);

    protected Task<decimal> ChannelBalance(string channel = "default") =>
        Ledger.GetBalanceAsync(new AccountRef(FinanceAccountCodes.ChannelClearing, FinOwnerType.Channel, channel), Cny);

    protected Task<T> ReadAsync<T>(Func<LedgerDbContext, Task<T>> query) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
            return await query(db);
        });

    protected Task<List<FinVoucher>> VouchersAsync(string bizRefId) =>
        ReadAsync(db => db.Vouchers.AsNoTracking().Where(v => v.BizRefId == bizRefId).OrderBy(v => v.CreationTime).ToListAsync());

    protected Task<List<FinEntry>> EntriesAsync(Guid voucherId) =>
        ReadAsync(db => db.Entries.AsNoTracking().Where(e => e.VoucherId == voucherId).OrderBy(e => e.Seq).ToListAsync());

    /// <summary>全账本按币种 Σ借 = Σ贷（从分录重算）。</summary>
    protected async Task AssertTrialBalancedAsync()
    {
        var sums = await ReadAsync(db => db.Entries.AsNoTracking()
            .GroupBy(_ => 1).Select(g => new { D = g.Sum(e => e.Debit), C = g.Sum(e => e.Credit) }).ToListAsync());
        foreach (var s in sums)
        {
            Shouldly.ShouldBeTestExtensions.ShouldBe(s.D, s.C);
        }
    }

    /// <summary>投影 = 分录重算（每个子户）。</summary>
    protected async Task AssertProjectionMatchesEntriesAsync()
    {
        var rows = await ReadAsync(async db =>
        {
            var subs = await db.SubAccounts.AsNoTracking().ToListAsync();
            var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
            var proj = await db.BalanceProjections.AsNoTracking().ToDictionaryAsync(p => p.SubAccountId);
            var sums = await db.Entries.AsNoTracking().GroupBy(e => e.SubAccountId)
                .Select(g => new { g.Key, D = g.Sum(e => e.Debit), C = g.Sum(e => e.Credit) }).ToDictionaryAsync(x => x.Key);
            return subs.Select(s =>
            {
                var a = accounts[s.AccountId];
                var sum = sums.TryGetValue(s.Id, out var x) ? (a.NormalSide == FinNormalSide.Debit ? x.D - x.C : x.C - x.D) : 0m;
                return (Expected: sum, Actual: proj.TryGetValue(s.Id, out var p) ? p.Balance : 0m);
            }).ToList();
        });
        foreach (var r in rows)
        {
            Shouldly.ShouldBeTestExtensions.ShouldBe(r.Actual, r.Expected);
        }
    }

    protected static FinanceBizEvent CreditPay(string refId, decimal amount, Guid user, string? batch = null) =>
        new(FinanceBizTypes.DemoCredit, refId, FinanceBizTypes.Phases.Pay, Cny, amount, UserId: user, BatchKey: batch);

    protected static FinanceBizEvent CreditRecognize(string refId, decimal amount, Guid user, string batch) =>
        new(FinanceBizTypes.DemoCredit, refId, FinanceBizTypes.Phases.Recognize, Cny, amount, UserId: user, BatchKey: batch);

    protected Task RunInNewUowAsync(Func<IServiceProvider, Task> action) =>
        Task.Run(async () =>
        {
            using var scope = ServiceProvider.CreateScope();
            using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            await action(scope.ServiceProvider);
            await uow.CompleteAsync();
        });
}
