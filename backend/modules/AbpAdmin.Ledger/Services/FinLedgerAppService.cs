using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>账本查询（余额 / 试算 / 子户余额）。演示过账统一走 FinDemoEventAppService
/// （POST /api/app/fin-demo-event）——此处曾有一份逐行同构的 post-demo-event 副本，为避免
/// 双契约漂移已删（OCR design 修复），文档口径见 docs/ledger-f1-decisions.md。</summary>
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

    [Authorize(FinancePermissions.Ledger.View)]
    public virtual async Task<FinBalanceDto> GetBalanceAsync(GetBalanceInput input)
    {
        // 代理切片（NV-02）：与 GetSubAccountBalancesAsync 同口径——持 Scope.Agent（且无 Scope.All）
        // 的调用方无视客户端传参，强制按自己的 AgentId 查 Agent 归属余额。此前本路径未调
        // ResolveAgentScopeAsync，任意 OwnerId 的余额可被切片代理原样读出（跨代理财务披露）。
        var agentScope = await ResolveAgentScopeAsync();
        var ownerType = agentScope != null ? FinOwnerType.Agent : input.OwnerType;
        var ownerId = agentScope?.ToString() ?? input.OwnerId ?? "";

        var balance = await _engine.GetBalanceAsync(
            new AccountRef(input.AccountCode, ownerType, ownerId, input.Bucket ?? ""),
            input.Currency);
        return new FinBalanceDto
        {
            AccountCode = input.AccountCode,
            OwnerType = ownerType,
            OwnerId = ownerId,
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
        var agentScope = await ResolveAgentScopeAsync();
        var agentId = agentScope ?? input.AgentId;

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

        if (agentId != null || agentScope != null)
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

    /// <summary>试算平衡：按科目×币种汇总分录借贷。Scope.Agent 调用方只汇总自己名下子户
    /// 的分录（NV-02，与余额路径同边界）。</summary>
    [Authorize(FinancePermissions.Ledger.View)]
    public virtual async Task<FinTrialBalanceDto> GetTrialBalanceAsync(FinTrialBalanceInput input)
    {
        var agentScope = await ResolveAgentScopeAsync();
        var db = await _dbContextProvider.GetDbContextAsync();
        var q = from e in db.Entries.AsNoTracking()
                join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
                join s in db.SubAccounts.AsNoTracking() on e.SubAccountId equals s.Id
                select new { e, v, s };
        if (agentScope != null)
        {
            var own = agentScope.Value.ToString();
            q = q.Where(x => x.s.OwnerType == FinOwnerType.Agent && x.s.OwnerId == own);
        }
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
