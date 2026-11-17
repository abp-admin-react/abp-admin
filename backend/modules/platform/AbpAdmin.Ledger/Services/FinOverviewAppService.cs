using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Authorization;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// 资金总览。今日额按凭证创建时间的 UTC 日界；试算、余额和收入按币种分开。
/// 只有一种币时，顶栏合计等于该币；多于一种币时顶栏合计为 0，以 CurrencyTotals 为准。
/// </summary>
[Authorize(FinancePermissions.Ledger.View)]
public class FinOverviewAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;

    public FinOverviewAppService(IDbContextProvider<LedgerDbContext> db) => _db = db;

    public virtual async Task<FinOverviewDto> GetAsync()
    {
        // 代理切片：本端点是平台级资金总览（全平台借贷合计/试算不变量/收入合计），
        // 不存在有意义的「代理切片」形态——对切片化切片求和会破坏 IsBalanced 全历史不变量。
        // 持 Scope.Agent 且无 Scope.All 的调用方整体拒绝（fail-closed），不泄露平台级财务面。
        if (await ResolveAgentScopeAsync() != null)
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);
        }

        var db = await _db.GetDbContextAsync();
        var today = Clock.Now.Date;
        var tomorrow = today.AddDays(1);

        var todayAgg = await (
            from e in db.Entries.AsNoTracking()
            join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            where v.CreationTime >= today && v.CreationTime < tomorrow && !v.IsDemo
            group e by new { v.Currency, v.Phase } into g
            select new { g.Key.Currency, g.Key.Phase, Credit = g.Sum(x => x.Credit), Debit = g.Sum(x => x.Debit) }).ToListAsync();

        var trialRows = await (
            from e in db.Entries.AsNoTracking()
            join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
            where !v.IsDemo
            group e by v.Currency into g
            select new { Currency = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync();

        var projections = await (
            from p in db.BalanceProjections.AsNoTracking()
            join s in db.SubAccounts.AsNoTracking() on p.SubAccountId equals s.Id
            join a in db.Accounts.AsNoTracking() on s.AccountId equals a.Id
            group p by new { s.Currency, a.Code, a.Type } into g
            select new { g.Key.Currency, g.Key.Code, g.Key.Type, Balance = g.Sum(x => x.Balance) }).ToListAsync();

        var currencies = todayAgg.Select(x => x.Currency)
            .Concat(trialRows.Select(x => x.Currency))
            .Concat(projections.Select(x => x.Currency))
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        var totals = currencies.Select(currency =>
        {
            var todayOf = todayAgg.Where(x => x.Currency == currency).ToList();
            var trial = trialRows.FirstOrDefault(x => x.Currency == currency);
            var of = projections.Where(x => x.Currency == currency).ToList();
            return new FinCurrencyTotalDto
            {
                Currency = currency,
                TodayIncome = todayOf.Where(x => FinanceBizTypes.IncomePhases.Contains(x.Phase)).Sum(x => x.Credit),
                TodayExpense = todayOf.Where(x => FinanceBizTypes.ExpensePhases.Contains(x.Phase)).Sum(x => x.Debit),
                TotalDebit = trial?.Debit ?? 0,
                TotalCredit = trial?.Credit ?? 0,
                IsBalanced = trial == null || trial.Debit == trial.Credit,
                Available = of.Where(x => x.Code == FinanceAccountCodes.UserAvailable).Sum(x => x.Balance),
                Frozen = of.Where(x => x.Code == FinanceAccountCodes.UserFrozen).Sum(x => x.Balance),
                Unearned = of.Where(x => x.Code == FinanceAccountCodes.Unearned).Sum(x => x.Balance),
                RecognizedIncome = of.Where(x => x.Type == FinAccountType.Income).Sum(x => x.Balance),
            };
        }).ToList();

        var unreconciled = await db.ReconcileRuns.AsNoTracking().CountAsync(r => r.HasDiff && !r.IncludeDemo);
        var single = totals.Count == 1 ? totals[0] : null;
        return new FinOverviewDto
        {
            TodayIncome = single?.TodayIncome ?? 0,
            TodayExpense = single?.TodayExpense ?? 0,
            IsBalanced = totals.All(x => x.IsBalanced),
            TotalDebit = single?.TotalDebit ?? 0,
            TotalCredit = single?.TotalCredit ?? 0,
            UnreconciledCount = unreconciled,
            AvailableTotal = single?.Available ?? 0,
            FrozenTotal = single?.Frozen ?? 0,
            UnearnedTotal = single?.Unearned ?? 0,
            RecognizedIncomeTotal = single?.RecognizedIncome ?? 0,
            CurrencyTotals = totals,
        };
    }
}
