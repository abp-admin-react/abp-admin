using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>A1 资金总览：GET /api/app/fin-overview</summary>
[Authorize(FinancePermissions.Ledger.View)]
public class FinOverviewAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;

    public FinOverviewAppService(IDbContextProvider<LedgerDbContext> db) => _db = db;

    public virtual async Task<FinOverviewDto> GetAsync()
    {
        var db = await _db.GetDbContextAsync();
        var today = Clock.Now.Date;
        var tomorrow = today.AddDays(1);

        var todayRows = await (
            from e in db.Entries.AsNoTracking()
            join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            where v.CreationTime >= today && v.CreationTime < tomorrow && !v.IsDemo
            select new { e.Debit, e.Credit, v.Phase }).ToListAsync();

        var todayIn = todayRows.Where(x => x.Phase is "pay" or "settled" or "recognize").Sum(x => x.Credit);
        var todayOut = todayRows.Where(x => x.Phase is "refund" or "settle").Sum(x => x.Debit);

        var trial = await (
            from e in db.Entries.AsNoTracking()
            join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            where !v.IsDemo
            select e).GroupBy(_ => 1)
            .Select(g => new { D = g.Sum(x => x.Debit), C = g.Sum(x => x.Credit) })
            .FirstOrDefaultAsync();

        var available = await SumProjectionAsync(db, FinanceAccountCodes.UserAvailable);
        var frozen = await SumProjectionAsync(db, FinanceAccountCodes.UserFrozen);
        var unearned = await SumProjectionAsync(db, FinanceAccountCodes.Unearned);
        var income = await SumProjectionAsync(db, FinanceAccountCodes.ServiceIncome)
                   + await SumProjectionAsync(db, FinanceAccountCodes.RentIncome);

        var unreconciled = await db.ReconcileRuns.AsNoTracking().CountAsync(r => r.HasDiff && !r.IncludeDemo);

        return new FinOverviewDto
        {
            TodayIncome = todayIn,
            TodayExpense = todayOut,
            IsBalanced = trial == null || trial.D == trial.C,
            TotalDebit = trial?.D ?? 0,
            TotalCredit = trial?.C ?? 0,
            UnreconciledCount = unreconciled,
            AvailableTotal = available,
            FrozenTotal = frozen,
            UnearnedTotal = unearned,
            RecognizedIncomeTotal = income,
        };
    }

    private static async Task<decimal> SumProjectionAsync(LedgerDbContext db, string code)
    {
        return await (
            from p in db.BalanceProjections.AsNoTracking()
            join s in db.SubAccounts.AsNoTracking() on p.SubAccountId equals s.Id
            join a in db.Accounts.AsNoTracking() on s.AccountId equals a.Id
            where a.Code == code
            select p.Balance).SumAsync();
    }
}
