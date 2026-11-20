using System.Text.Json.Serialization;
using AbpAdmin.Grids;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.MultiTenancy;
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
/// 双契约漂移已删，文档口径见 docs/ledger-f1-decisions.md。</summary>
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

    public sealed class BalanceGridRow
    {
        public string Id { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public string? OwnerName { get; set; }
        public string Currency { get; set; } = "";
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public decimal Available { get; set; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public decimal Frozen { get; set; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public decimal Prepayment { get; set; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public decimal LedgerDerived { get; set; }
    }

    /// <summary>用户按归属与币种合并，其他归属按子户列出。在数据库内完成聚合、过滤及分页。</summary>
    [HttpPost("api/app/fin-ledger/balance-projections/query")]
    public virtual async Task<PagedResultDto<BalanceGridRow>> QueryBalanceProjectionsAsync([FromBody] GridQueryInput input)
    {
        await RequireAnyAsync(null, FinancePermissions.Ledger.View, FinancePermissions.Balance.View);
        var agent = await ResolveAgentScopeAsync();
        var tenant = CurrentTenant.Id;
        var db = await _dbContextProvider.GetDbContextAsync();
        using (DataFilter.Disable<IMultiTenant>())
        {
            // 科目模板可来自宿主，子户和投影始终限定当前租户。
            var rows = from sub in db.SubAccounts.AsNoTracking()
                       join projection in db.BalanceProjections on sub.Id equals projection.SubAccountId
                       join account in db.Accounts on sub.AccountId equals account.Id
                       where sub.TenantId == tenant && projection.TenantId == tenant
                             && (account.TenantId == tenant || account.TenantId == null)
                       select new { sub, account.Code, projection.Balance };
            if (agent.HasValue)
            {
                var ownerId = agent.Value.ToString();
                rows = rows.Where(r => r.sub.OwnerType == FinOwnerType.Agent && r.sub.OwnerId == ownerId);
            }
            var grouped = from row in rows
                          group row by new { row.sub.OwnerType, row.sub.OwnerId, row.sub.Currency,
                              SubId = row.sub.OwnerType == FinOwnerType.User ? (Guid?)null : row.sub.Id,
                              Code = row.sub.OwnerType == FinOwnerType.User ? null : row.Code } into g
                          select new BalanceGridRow
                          {
                              Id = g.Key.SubId == null ? g.Key.OwnerId + "|" + g.Key.Currency : g.Key.SubId.ToString()!,
                              OwnerId = g.Key.OwnerId, OwnerName = g.Key.Code, Currency = g.Key.Currency,
                              Available = g.Sum(r => r.sub.OwnerType != FinOwnerType.User || r.Code == "2201" ? r.Balance : 0m),
                              Frozen = g.Sum(r => r.sub.OwnerType == FinOwnerType.User && r.Code == "2202" ? r.Balance : 0m),
                              Prepayment = g.Sum(r => r.sub.OwnerType == FinOwnerType.User && r.Code == "2205" ? r.Balance : 0m),
                              LedgerDerived = g.Sum(r => r.sub.OwnerType != FinOwnerType.User || r.Code == "2201" || r.Code == "2202" || r.Code == "2205" ? r.Balance : 0m),
                          };
            var definition = new GridQueryDefinition<BalanceGridRow>()
                .SearchableField("ownerName", x => x.OwnerName).SearchableField("ownerId", x => x.OwnerId)
                .SearchableField("currency", x => x.Currency).Field("available", x => x.Available)
                .Field("frozen", x => x.Frozen).Field("prepayment", x => x.Prepayment)
                .Field("ledgerDerived", x => x.LedgerDerived);
            var query = definition.Filter(grouped, input);
            var total = await query.LongCountAsync();
            var items = await definition.OrderBy(query, input, "ownerId", false, x => x.Id)
                .Skip(input.SkipCount).Take(input.MaxResultCount).ToListAsync();
            return new(total, items);
        }
    }

    [Authorize(FinancePermissions.Ledger.View)]
    public virtual async Task<FinBalanceDto> GetBalanceAsync(GetBalanceInput input)
    {
        // 代理切片：与 GetSubAccountBalancesAsync 同口径——持 Scope.Agent（且无 Scope.All）
        // 的调用方无视客户端传参，强制按自己的 AgentId 查 Agent 归属余额。本路径若不调
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
    /// 的分录（与余额路径同边界）。</summary>
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
