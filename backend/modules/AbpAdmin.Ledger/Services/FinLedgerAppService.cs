using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>账本查询（余额 / 试算 / 子户余额）+ 演示事件入口。</summary>
public class FinLedgerAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _dbContextProvider;
    private readonly LedgerPostingEngine _engine;
    private readonly PostingRuleProvider _rules;

    public FinLedgerAppService(
        IDbContextProvider<LedgerDbContext> dbContextProvider,
        LedgerPostingEngine engine,
        PostingRuleProvider rules)
    {
        _dbContextProvider = dbContextProvider;
        _engine = engine;
        _rules = rules;
    }

    /// <summary>
    /// 演示事件过账：仅 BizType 以 "demo." 开头。
    /// 路由固定 POST /api/app/fin-ledger/post-demo-event。
    /// </summary>
    [HttpPost("post-demo-event")]
    public virtual async Task<PostResultDto> CreateDemoEventAsync(PostDemoEventInput input)
    {
        await RequireAnyAsync(null, FinancePermissions.Adjust.Create);
        if (!input.BizType.StartsWith("demo.", StringComparison.Ordinal))
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent)
                .WithData("Field", "BizType must start with demo.");
        }

        var id = await _engine.PostAsync(new FinanceBizEvent(
            input.BizType, input.BizRefId, input.Phase, input.Currency, input.Amount,
            input.UserId, input.AgentId, Dimensions: input.Dimensions, Summary: input.Summary,
            OperatorId: CurrentUser.Id, BatchKey: input.BatchKey));
        return new PostResultDto { VoucherId = id };
    }

    [Authorize(FinancePermissions.Ledger.View)]
    public virtual async Task<FinBalanceDto> GetBalanceAsync(GetBalanceInput input)
    {
        var balance = await _engine.GetBalanceAsync(
            new AccountRef(input.AccountCode, input.OwnerType, input.OwnerId ?? "", input.Bucket ?? ""),
            input.Currency);
        return new FinBalanceDto
        {
            AccountCode = input.AccountCode,
            OwnerType = input.OwnerType,
            OwnerId = input.OwnerId ?? "",
            Currency = input.Currency,
            Balance = balance,
        };
    }

    /// <summary>
    /// 子户余额列表（A7）。持 Finance.Scope.Agent 时强制只看自己。
    /// </summary>
    public virtual async Task<PagedResultDto<FinBalanceDto>> GetSubAccountBalancesAsync(FinSubAccountBalanceListInput input)
    {
        await RequireAnyAsync(null, FinancePermissions.Ledger.View, FinancePermissions.Balance.View);
        var agentScoped = await AuthorizationService.IsGrantedAsync(FinancePermissions.Scope.Agent);
        var agentId = agentScoped ? RequireUserId() : input.AgentId;

        var db = await _dbContextProvider.GetDbContextAsync();
        var q = from s in db.SubAccounts.AsNoTracking()
                join p in db.BalanceProjections.AsNoTracking() on s.Id equals p.SubAccountId
                select new { s, p };
        if (!string.IsNullOrWhiteSpace(input.AccountCode))
        {
            var acc = await _rules.GetAccountAsync(input.AccountCode);
            q = q.Where(x => x.s.AccountId == acc.Id);
        }

        if (!string.IsNullOrWhiteSpace(input.Currency))
        {
            q = q.Where(x => x.s.Currency == input.Currency);
        }

        if (agentId != null || agentScoped)
        {
            var owner = agentId?.ToString() ?? "";
            q = q.Where(x => x.s.OwnerType == FinOwnerType.Agent && x.s.OwnerId == owner);
        }
        else if (!string.IsNullOrWhiteSpace(input.OwnerId))
        {
            q = q.Where(x => x.s.OwnerId == input.OwnerId);
        }

        var total = await q.CountAsync();
        var (skip, take) = Paging(input);
        var rows = await q.OrderBy(x => x.s.AccountId).ThenBy(x => x.s.OwnerId).Skip(skip).Take(take).ToListAsync();
        var accounts = await _rules.GetAccountsByIdAsync(rows.Select(r => r.s.AccountId));
        return new PagedResultDto<FinBalanceDto>(total, rows.Select(r => new FinBalanceDto
        {
            AccountCode = accounts[r.s.AccountId].Code,
            OwnerType = r.s.OwnerType,
            OwnerId = r.s.OwnerId,
            Currency = r.s.Currency,
            Balance = r.p.Balance,
        }).ToList());
    }

    /// <summary>试算平衡：按科目×币种汇总分录借贷。</summary>
    [Authorize(FinancePermissions.Ledger.View)]
    public virtual async Task<FinTrialBalanceDto> GetTrialBalanceAsync(FinTrialBalanceInput input)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var q = from e in db.Entries.AsNoTracking()
                join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
                join s in db.SubAccounts.AsNoTracking() on e.SubAccountId equals s.Id
                select new { e, v, s };
        if (!input.IncludeDemo)
        {
            q = q.Where(x => !x.v.IsDemo);
        }

        if (!string.IsNullOrWhiteSpace(input.Currency))
        {
            q = q.Where(x => x.s.Currency == input.Currency);
        }

        var grouped = await q.GroupBy(x => new { x.s.AccountId, x.s.Currency })
            .Select(g => new { g.Key.AccountId, g.Key.Currency, Debit = g.Sum(x => x.e.Debit), Credit = g.Sum(x => x.e.Credit) })
            .ToListAsync();
        var accounts = await _rules.GetAccountsByIdAsync(grouped.Select(g => g.AccountId));
        var lines = grouped.Select(g => new FinTrialBalanceLineDto
        {
            AccountCode = accounts[g.AccountId].Code,
            AccountName = accounts[g.AccountId].Name,
            Currency = g.Currency,
            Debit = g.Debit,
            Credit = g.Credit,
        }).OrderBy(l => l.Currency).ThenBy(l => l.AccountCode).ToList();
        var balanced = lines.GroupBy(l => l.Currency).All(c => c.Sum(l => l.Debit) == c.Sum(l => l.Credit));
        return new FinTrialBalanceDto
        {
            Lines = lines,
            TotalDebit = lines.Sum(l => l.Debit),
            TotalCredit = lines.Sum(l => l.Credit),
            IsBalanced = balanced,
        };
    }
}
